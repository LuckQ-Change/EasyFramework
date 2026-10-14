using EasyFramework.UI;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.Tests
{
    public sealed class UIManagerTests
    {
        private sealed class TestView : UIView { public TestView() { } }
        [UIPrefab("UI/Canonical")]
        private sealed class AttributedView : UIView { public AttributedView() { } }
        private sealed class StatefulView : UIView<int>
        {
            public int OpenedWith { get; private set; }
            public bool Closed { get; private set; }
            public bool Disposed { get; private set; }

            protected override void OnOpened(int args) => OpenedWith = args;
            protected override void OnClosed() => Closed = true;
            protected override void OnDispose() => Disposed = true;
        }
        private class Item : UIItem { public Item() { } }
        private sealed class CostItem : Item { public CostItem() { } }

        private sealed class UIPrefabLoader : IAssetLoader
        {
            private readonly GameObject _prefab;
            public int LoadCalls { get; private set; }
            public int ReleaseCalls { get; private set; }
            public UIPrefabLoader(GameObject prefab) => _prefab = prefab;
            public Task<bool> InitializeAsync() => Task.FromResult(true);
            public Task<T> LoadAsync<T>(string location) where T : class
            {
                LoadCalls++;
                return Task.FromResult(_prefab as T);
            }
            public void Release(string location) => ReleaseCalls++;
            public void ReleaseAll() { }
        }

        private static UIDisplay CreateDisplayPrefab(string name, string recordId)
        {
            var prefab = new GameObject(name, typeof(RectTransform), typeof(UIDisplay));
            var display = prefab.GetComponent<UIDisplay>();
            display.SetViewScript(
                typeof(TestView).FullName,
                string.Empty,
                "UI/" + name);
            return display;
        }

        [Test]
        public void Open_ReusesSingleton_UsesRequestedLayer_AndBackClosesIt()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("Inventory", "inventory-test");
            try
            {
                var manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(UIManager)));
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(UIView)));
                UIView first = manager.Open(prefab.gameObject, 1, UILayer.Popup);
                UIView second = manager.Open(prefab.gameObject, 2, UILayer.Popup);

                Assert.AreSame(first, second);
                Assert.AreEqual(
                    1,
                    manager.OpenCount,
                    "Open displays: " + string.Join(", ", manager.OpenDisplays));
                Assert.AreEqual(2, first.OpenArgs);
                Assert.AreEqual(UILayer.Popup, first.Display.CurrentLayer);
                Assert.AreSame(manager.GetLayerRoot(UILayer.Popup), first.Display.transform.parent);
                Assert.AreEqual((int)UILayer.Popup,
                    manager.GetLayerRoot(UILayer.Popup).GetComponent<Canvas>().sortingOrder);
                Assert.AreSame(first, UIService.Get<TestView>());
                Assert.IsTrue(UIService.IsOpen<TestView>());
                Assert.Throws<NotSupportedException>(
                    () => ((IList<UIDisplay>)manager.OpenDisplays).Clear());
                Assert.IsTrue(UIService.Back());
                Assert.AreEqual(0, manager.OpenCount);
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void Open_ReusingSingleton_RestartsItsTransition()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("Transitioned", "transitioned-test");
            CanvasGroup prefabGroup = prefab.gameObject.AddComponent<CanvasGroup>();
            prefabGroup.alpha = 0.65f;
            prefab.gameObject.AddComponent<UITransition>();
            try
            {
                UIManager manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                TestView first = manager.Open<TestView>(prefab.gameObject);
                CanvasGroup group = first.Display.GetComponent<CanvasGroup>();
                Assert.AreEqual(0.65f, group.alpha, 0.0001f);

                group.alpha = 0.2f;
                group.blocksRaycasts = false;
                TestView reopened = manager.Open<TestView>(prefab.gameObject);

                Assert.AreSame(first, reopened);
                Assert.AreEqual(1, manager.OpenCount);
                Assert.AreEqual(0.65f, group.alpha, 0.0001f);
                Assert.IsTrue(group.blocksRaycasts);
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void Display_PrefabLocation_UsesViewAttributeAsCanonicalSource()
        {
            var gameObject = new GameObject("Canonical", typeof(UIDisplay));
            try
            {
                UIDisplay display = gameObject.GetComponent<UIDisplay>();
                display.SetViewScript(
                    typeof(AttributedView).AssemblyQualifiedName,
                    string.Empty,
                    "UI/Stale");

                Assert.AreEqual("UI/Canonical", display.PrefabLocation);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Validator_ReportsInvisibleCanvasScale()
        {
            var root = new GameObject("Validation", typeof(RectTransform), typeof(UIDisplay));
            var child = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            child.transform.SetParent(root.transform, false);
            child.transform.localScale = new Vector3(0f, 1f, 1f);
            try
            {
                Type validator = Type.GetType(
                    "EasyFramework.Editor.UI.UIValidator, com.wjq.easyframework.editor");
                Assert.NotNull(validator);
                var validate = validator.GetMethod("Validate",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Assert.NotNull(validate);
                var problems = (List<string>)validate.Invoke(null,
                    new object[] { root.GetComponent<UIDisplay>() });
                Assert.IsTrue(problems.Exists(message => message.Contains("缩放为 0")));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Validator_ReportsZeroSizedNestedCanvas()
        {
            var root = new GameObject("Validation", typeof(RectTransform), typeof(UIDisplay));
            var child = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            child.transform.SetParent(root.transform, false);
            var rect = child.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            try
            {
                Type validator = Type.GetType(
                    "EasyFramework.Editor.UI.UIValidator, com.wjq.easyframework.editor");
                var validate = validator.GetMethod("Validate",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var problems = (List<string>)validate.Invoke(null,
                    new object[] { root.GetComponent<UIDisplay>() });
                Assert.IsTrue(problems.Exists(message => message.Contains("宽或高为 0")));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Validator_AcceptsGeneratedViewNameWithoutAssemblyQualifier()
        {
            var gameObject = new GameObject("Generated View", typeof(UIDisplay));
            try
            {
                UIDisplay display = gameObject.GetComponent<UIDisplay>();
                display.SetViewScript(typeof(AttributedView).FullName, string.Empty, "UI/Stale");
                Type validator = Type.GetType(
                    "EasyFramework.Editor.UI.UIValidator, com.wjq.easyframework.editor");
                Assert.NotNull(validator);
                var method = validator.GetMethod("Validate",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Assert.NotNull(method);
                var problems = (List<string>)method.Invoke(null, new object[] { display });
                Assert.IsEmpty(problems);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DisplayInspector_AcceptsNewlyGeneratedFullTypeName()
        {
            Type inspector = Type.GetType(
                "EasyFramework.Editor.UI.UIDisplayInspector, com.wjq.easyframework.editor");
            Assert.NotNull(inspector);
            var matches = inspector.GetMethod("MatchesStoredTypeName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(matches);
            Type viewType = typeof(AttributedView);
            Assert.IsTrue((bool)matches.Invoke(null, new object[] { viewType.FullName, viewType }));
            Assert.IsTrue((bool)matches.Invoke(null, new object[]
                { viewType.FullName + ", " + viewType.Assembly.GetName().Name, viewType }));
            Assert.IsFalse((bool)matches.Invoke(null, new object[]
                { viewType.FullName + ", WrongAssembly", viewType }));
        }

        [Test]
        public void Validator_IssuesPointToTheComponentThatNeedsRepair()
        {
            var root = new GameObject("Validation", typeof(UIDisplay), typeof(UIReference));
            try
            {
                UIDisplay display = root.GetComponent<UIDisplay>();
                display.SetViewScript(typeof(AttributedView).FullName, string.Empty, "UI/Canonical");
                UIReference marker = root.GetComponent<UIReference>();
                var serialized = new SerializedObject(marker);
                SerializedProperty entries = serialized.FindProperty("_entries");
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("_key").stringValue = "Missing";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Type validator = Type.GetType(
                    "EasyFramework.Editor.UI.UIValidator, com.wjq.easyframework.editor");
                Assert.NotNull(validator);
                var method = validator.GetMethod("FindIssues",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.NotNull(method);
                var issues = (IEnumerable)method.Invoke(null, new object[] { display });
                bool found = false;
                foreach (object issue in issues)
                {
                    string message = (string)issue.GetType().GetProperty("Message").GetValue(issue);
                    if (!message.Contains("Reference Target")) continue;
                    Assert.AreSame(marker, issue.GetType().GetProperty("Context").GetValue(issue));
                    found = true;
                }
                Assert.IsTrue(found);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CreationWizard_CreatesPrefabLinkedToExistingViewScript()
        {
            string folder = "Assets/__EasyFrameworkWizard_" + Guid.NewGuid().ToString("N");
            string prefabPath = folder + "/WizardAcceptanceView.prefab";
            Assert.NotNull(AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length)));
            GameObject contents = null;
            try
            {
                Type wizard = Type.GetType(
                    "EasyFramework.Editor.UI.UICreationWizard, com.wjq.easyframework.editor");
                Assert.NotNull(wizard);
                var create = wizard.GetMethod("CreateAssets",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.NotNull(create);
                var prefab = (GameObject)create.Invoke(null, new object[]
                {
                    prefabPath, "WizardAcceptanceView", "EasyFramework.Tests",
                    "UI/WizardAcceptanceView", "Assets/EasyFramework/Tests/Editor", true
                });
                Assert.NotNull(prefab);
                Assert.AreEqual(prefabPath, AssetDatabase.GetAssetPath(prefab));

                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                UIDisplay display = contents.GetComponent<UIDisplay>();
                Assert.NotNull(display);
                Assert.IsTrue(display.ManagedAsView);
                Assert.AreEqual("UI/WizardAcceptanceView", display.PrefabLocation);
                Assert.AreEqual("Assets/EasyFramework/Tests/Editor/WizardAcceptanceView.cs",
                    display.ViewScriptPath);
                Assert.IsTrue(display.ViewTypeName.Contains(typeof(WizardAcceptanceView).FullName));
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SetLayer_HideAndShow_UpdateTopView()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay firstPrefab = CreateDisplayPrefab("First", "first-test");
            UIDisplay secondPrefab = CreateDisplayPrefab("Second", "second-test");
            try
            {
                var manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                UIView first = manager.Open(firstPrefab.gameObject, layer: UILayer.System);
                UIView second = manager.Open(secondPrefab.gameObject, layer: UILayer.Screen);
                Assert.AreSame(first, manager.TopView);
                Assert.IsTrue(first.IsVisible);
                first.Hide();
                Assert.IsFalse(first.IsVisible);
                Assert.AreSame(second, manager.TopView);
                manager.SetLayer(second, UILayer.System);
                Assert.AreEqual(UILayer.System, second.Display.CurrentLayer);
                first.Show();
                Assert.IsTrue(first.IsVisible);
                Assert.AreSame(first, manager.TopView);
            }
            finally
            {
                Object.DestroyImmediate(firstPrefab.gameObject);
                Object.DestroyImmediate(secondPrefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void Open_AppliesConfiguredUnityLayerRecursively()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("Layered", "layered-test");
            var child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(prefab.transform, false);
            prefab.gameObject.layer = 0;
            child.layer = 0;

            try
            {
                UIManager manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                UIView view = manager.Open(prefab.gameObject);
                int expectedLayer = manager.RuntimeHost.UILayer;

                Assert.AreEqual(expectedLayer, view.Display.gameObject.layer);
                Assert.AreEqual(expectedLayer, view.Display.transform.GetChild(0).gameObject.layer);
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void Open_RemovesConflictingCanvasComponentsFromManagedViewRoot()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("Canvas View", "canvas-view-test");
            prefab.gameObject.AddComponent<Canvas>();
            prefab.gameObject.AddComponent<CanvasScaler>();
            prefab.gameObject.AddComponent<GraphicRaycaster>();

            try
            {
                UIManager manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                UIView view = manager.Open(prefab.gameObject);

                Assert.IsNull(view.Display.GetComponent<Canvas>());
                Assert.IsNull(view.Display.GetComponent<CanvasScaler>());
                Assert.IsNull(view.Display.GetComponent<GraphicRaycaster>());
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [UnityTest]
        public IEnumerator PreloadAndOpenAsync_ShareAssetModuleLoad_AndReleaseBothLeases()
        {
            Task task = PreloadAndOpenCore();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
        }

        private static async Task PreloadAndOpenCore()
        {
            var modules = new ModuleManager();
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("AsyncInventory", "async-inventory-test");
            UIManager ui = null;
            try
            {
                modules.Register<AssetModule>();
                modules.InitAll();
                var loader = new UIPrefabLoader(prefab.gameObject);
                AssetModule.Instance.SetLoader(loader);
                Assert.IsTrue(await AssetModule.Instance.InitializeAsync());
                ui = managerObject.GetComponent<UIRuntimeHost>().Manager;
                Assert.IsTrue(await ui.PreloadAsync("UI/AsyncInventory"));
                TestView view = await UIService.OpenAtAsync<TestView>("UI/AsyncInventory");
                Assert.NotNull(view);
                Assert.AreEqual(1, loader.LoadCalls);
                Assert.IsTrue(ui.Close(view));
                Assert.AreEqual(0, loader.ReleaseCalls);
                Assert.IsTrue(ui.ReleasePreloaded("UI/AsyncInventory"));
                Assert.AreEqual(1, loader.ReleaseCalls);
            }
            finally
            {
                ui?.CloseAll();
                ui?.ReleaseAllPreloaded();
                if (managerObject != null) Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(prefab.gameObject);
                modules.ShutdownAll();
            }
        }

        [Test]
        public void Display_CreatesInheritedPureCSharpItem()
        {
            var itemObject = new GameObject("Cost Item", typeof(RectTransform), typeof(UIDisplay));
            try
            {
                UIDisplay display = itemObject.GetComponent<UIDisplay>();
                var serialized = new SerializedObject(display);
                serialized.FindProperty("_managedAsView").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                display.SetViewScript(typeof(CostItem).FullName, string.Empty, string.Empty);

                CostItem item = display.GetLogic<CostItem>();
                Assert.NotNull(item);
                Assert.IsInstanceOf<Item>(item);
                Assert.AreSame(display, item.Display);
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(item.GetType()));
            }
            finally
            {
                Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        public void TypedView_IsBindingSource_AndOwnsItsLifecycle()
        {
            var prefab = new GameObject("Stateful View", typeof(RectTransform), typeof(UIDisplay));
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            StatefulView view = null;
            try
            {
                var display = prefab.GetComponent<UIDisplay>();
                display.SetViewScript(typeof(StatefulView).FullName, string.Empty, string.Empty);

                view = managerObject.GetComponent<UIRuntimeHost>().Manager
                    .Open<StatefulView>(prefab, 42);

                Assert.AreSame(view, view.BindingSource);
                Assert.AreEqual(42, view.OpenedWith);
                Assert.IsTrue(view.Manager.Close(view));
                Assert.IsTrue(view.Closed);
                Assert.IsTrue(view.Disposed);
            }
            finally
            {
                if (prefab != null) Object.DestroyImmediate(prefab);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void Binding_Get_UsesPrefabSerializedReference()
        {
            var prefab = new GameObject("Reference View", typeof(RectTransform), typeof(UIDisplay));
            var buttonObject = new GameObject("Close", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(prefab.transform, false);
            var marker = prefab.AddComponent<UIReference>();
            var serializedMarker = new SerializedObject(marker);
            SerializedProperty entries = serializedMarker.FindProperty("_entries");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("_key").stringValue = "CloseButton";
            entry.FindPropertyRelative("_target").objectReferenceValue = buttonObject.GetComponent<Button>();
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            try
            {
                UIDisplay display = prefab.GetComponent<UIDisplay>();
                display.SetViewScript(typeof(TestView).FullName, string.Empty, string.Empty);
                TestView view = managerObject.GetComponent<UIRuntimeHost>().Manager.Open<TestView>(prefab);

                Button resolved = view.Binding.Get<Button>("CloseButton");
                Assert.NotNull(resolved);
                Assert.AreSame(view.Display.transform.Find("Close").GetComponent<Button>(), resolved);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [UnityTest]
        public IEnumerator SavedPrefab_ReferenceStateAndUIService_FormCompleteCreationPath()
        {
            Task task = VerifySavedPrefabCreationPath();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
        }

        private static async Task VerifySavedPrefabCreationPath()
        {
            string folder = "Assets/__EasyFrameworkAcceptance_" + Guid.NewGuid().ToString("N");
            string prefabPath = folder + "/AcceptanceView.prefab";
            Assert.NotNull(AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length)));
            var root = new GameObject("AcceptanceView", typeof(RectTransform),
                typeof(UIDisplay), typeof(UIStateController), typeof(UIReference));
            var buttonObject = new GameObject("ActionButton", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(UIElement));
            buttonObject.transform.SetParent(root.transform, false);
            var modules = new ModuleManager();
            GameObject managerObject = null;
            UIManager manager = null;
            try
            {
                UIDisplay display = root.GetComponent<UIDisplay>();
                display.SetViewScript(typeof(AttributedView).FullName, string.Empty, "UI/Canonical");
                var marker = root.GetComponent<UIReference>();
                var serialized = new SerializedObject(marker);
                SerializedProperty entries = serialized.FindProperty("_entries");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("_key").stringValue = "ActionButton";
                entry.FindPropertyRelative("_target").objectReferenceValue = buttonObject.GetComponent<Button>();
                serialized.ApplyModifiedPropertiesWithoutUndo();

                UIStateController controller = root.GetComponent<UIStateController>();
                UIElement element = buttonObject.GetComponent<UIElement>();
                element.Controller = controller;
                Image image = buttonObject.GetComponent<Image>();
                image.color = Color.white;
                element.CaptureDefault();
                image.color = Color.red;
                element.CaptureVariant("Disabled");
                element.ApplyDefaultOnly();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Assert.NotNull(prefab);
                modules.Register<AssetModule>();
                modules.InitAll();
                var loader = new UIPrefabLoader(prefab);
                AssetModule.Instance.SetLoader(loader);
                Assert.IsTrue(await AssetModule.Instance.InitializeAsync());
                managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
                manager = managerObject.GetComponent<UIRuntimeHost>().Manager;

                AttributedView view = await UIService.OpenAsync<AttributedView>();
                Assert.NotNull(view);
                Button button = view.Binding.Get<Button>("ActionButton");
                Assert.NotNull(button);
                Assert.IsTrue(view.Display.GetComponent<UIStateController>().SetState("Disabled"));
                Assert.AreEqual(Color.red, button.GetComponent<Image>().color);
                Assert.IsTrue(UIService.Close(view));
                Assert.AreEqual(0, manager.OpenCount);
                Assert.AreEqual(1, loader.ReleaseCalls);
            }
            finally
            {
                manager?.CloseAll();
                if (managerObject != null) Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(root);
                modules.ShutdownAll();
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void PrefabContents_ReferenceEditSurvivesUndoRedoAndReload()
        {
            string folder = "Assets/__EasyFrameworkUndo_" + Guid.NewGuid().ToString("N");
            string prefabPath = folder + "/UndoView.prefab";
            Assert.NotNull(AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length)));
            var source = new GameObject("UndoView", typeof(RectTransform), typeof(UIDisplay),
                typeof(UIReference), typeof(UIStateController));
            GameObject contents = null;
            try
            {
                source.GetComponent<UIDisplay>().SetViewScript(
                    typeof(AttributedView).FullName, string.Empty, "UI/Canonical");
                var button = new GameObject("Button", typeof(RectTransform), typeof(Button));
                button.transform.SetParent(source.transform, false);
                var marker = source.GetComponent<UIReference>();
                var serialized = new SerializedObject(marker);
                SerializedProperty entries = serialized.FindProperty("_entries");
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("_key").stringValue = "Before";
                entries.GetArrayElementAtIndex(0).FindPropertyRelative("_target").objectReferenceValue =
                    button.GetComponent<Button>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.NotNull(PrefabUtility.SaveAsPrefabAsset(source, prefabPath));

                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                marker = contents.GetComponent<UIReference>();
                serialized = new SerializedObject(marker);
                serialized.FindProperty("_entries").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("_key").stringValue = "After";
                Assert.IsTrue(serialized.ApplyModifiedProperties());
                Assert.AreEqual("After", marker.Entries[0].Key);

                Undo.PerformUndo();
                Assert.AreEqual("Before", marker.Entries[0].Key);
                Undo.PerformRedo();
                Assert.AreEqual("After", marker.Entries[0].Key);

                Assert.NotNull(PrefabUtility.SaveAsPrefabAsset(contents, prefabPath));
                PrefabUtility.UnloadPrefabContents(contents);
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                Assert.AreEqual("After", contents.GetComponent<UIReference>().Entries[0].Key);
                Assert.NotNull(contents.GetComponent<UIStateController>());
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ModalDisplay_UsesOneSharedBlockingBackground()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(UIRuntimeHost));
            UIDisplay prefab = CreateDisplayPrefab("Modal", "modal-test");
            try
            {
                var serialized = new SerializedObject(prefab);
                serialized.FindProperty("_backgroundMode").enumValueIndex = (int)UIBackgroundMode.Modal;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UIManager manager = managerObject.GetComponent<UIRuntimeHost>().Manager;
                manager.Open(prefab.gameObject, layer: UILayer.Popup);
                RectTransform layer = manager.GetLayerRoot(UILayer.Popup);
                Transform background = layer.Find("[Background]");

                Assert.NotNull(background);
                Assert.AreEqual(1, layer.GetComponentsInChildren<Image>(true).Length);
                Assert.IsTrue(background.GetComponent<Image>().raycastTarget);
                Assert.Less(background.GetSiblingIndex(), manager.TopDisplay.transform.GetSiblingIndex());
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }
    }
}
