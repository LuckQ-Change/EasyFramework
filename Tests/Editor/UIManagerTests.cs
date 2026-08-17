using EasyFramework.UI;
using NUnit.Framework;
using System.Collections;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class UIManagerTests
    {
        private sealed class TestView : EasyUIView { public TestView() { } }
        private sealed class StatefulView : EasyUIView<int>
        {
            public int OpenedWith { get; private set; }
            public bool Closed { get; private set; }
            public bool Disposed { get; private set; }

            protected override void OnOpened(int args) => OpenedWith = args;
            protected override void OnClosed() => Closed = true;
            protected override void OnDispose() => Disposed = true;
        }
        private class Item : EasyUIItem { public Item() { } }
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

        private static EasyUIDisplay CreateDisplayPrefab(string name, string recordId)
        {
            var prefab = new GameObject(name, typeof(RectTransform), typeof(EasyUIDisplay));
            var display = prefab.GetComponent<EasyUIDisplay>();
            display.SetViewScript(
                typeof(TestView).FullName,
                string.Empty,
                "UI/" + name);
            return display;
        }

        [Test]
        public void Open_ReusesSingleton_UsesRequestedLayer_AndBackClosesIt()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay prefab = CreateDisplayPrefab("Inventory", "inventory-test");
            try
            {
                var manager = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(EasyUIManager)));
                Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(EasyUIView)));
                EasyUIView first = manager.Open(prefab.gameObject, 1, UILayer.Popup);
                EasyUIView second = manager.Open(prefab.gameObject, 2, UILayer.Popup);

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
                Assert.IsTrue(manager.Back());
                Assert.AreEqual(0, manager.OpenCount);
            }
            finally
            {
                Object.DestroyImmediate(prefab.gameObject);
                if (managerObject != null) Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void SetLayer_HideAndShow_UpdateTopView()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay firstPrefab = CreateDisplayPrefab("First", "first-test");
            EasyUIDisplay secondPrefab = CreateDisplayPrefab("Second", "second-test");
            try
            {
                var manager = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
                EasyUIView first = manager.Open(firstPrefab.gameObject, layer: UILayer.System);
                EasyUIView second = manager.Open(secondPrefab.gameObject, layer: UILayer.Screen);
                Assert.AreSame(first, manager.TopView);
                manager.Hide(first);
                Assert.AreSame(second, manager.TopView);
                manager.SetLayer(second, UILayer.System);
                Assert.AreEqual(UILayer.System, second.Display.CurrentLayer);
                manager.Show(first);
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
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay prefab = CreateDisplayPrefab("Layered", "layered-test");
            var child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(prefab.transform, false);
            prefab.gameObject.layer = 0;
            child.layer = 0;

            try
            {
                EasyUIManager manager = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
                EasyUIView view = manager.Open(prefab.gameObject);
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
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay prefab = CreateDisplayPrefab("Canvas View", "canvas-view-test");
            prefab.gameObject.AddComponent<Canvas>();
            prefab.gameObject.AddComponent<CanvasScaler>();
            prefab.gameObject.AddComponent<GraphicRaycaster>();

            try
            {
                EasyUIManager manager = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
                EasyUIView view = manager.Open(prefab.gameObject);

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
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay prefab = CreateDisplayPrefab("AsyncInventory", "async-inventory-test");
            EasyUIManager ui = null;
            try
            {
                modules.Register<AssetModule>();
                modules.InitAll();
                var loader = new UIPrefabLoader(prefab.gameObject);
                AssetModule.Instance.SetLoader(loader);
                Assert.IsTrue(await AssetModule.Instance.InitializeAsync());
                ui = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
                Assert.IsTrue(await ui.PreloadAsync("UI/AsyncInventory"));
                TestView view = await ui.OpenAsync<TestView>("UI/AsyncInventory");
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
            var itemObject = new GameObject("Cost Item", typeof(RectTransform), typeof(EasyUIDisplay));
            try
            {
                EasyUIDisplay display = itemObject.GetComponent<EasyUIDisplay>();
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
            var prefab = new GameObject("Stateful View", typeof(RectTransform), typeof(EasyUIDisplay));
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            StatefulView view = null;
            try
            {
                var display = prefab.GetComponent<EasyUIDisplay>();
                display.SetViewScript(typeof(StatefulView).FullName, string.Empty, string.Empty);

                view = managerObject.GetComponent<EasyUIRuntimeHost>().Manager
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
            var prefab = new GameObject("Reference View", typeof(RectTransform), typeof(EasyUIDisplay));
            var buttonObject = new GameObject("Close", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(prefab.transform, false);
            var marker = prefab.AddComponent<EasyUIReference>();
            var serializedMarker = new SerializedObject(marker);
            SerializedProperty entries = serializedMarker.FindProperty("_entries");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("_key").stringValue = "CloseButton";
            entry.FindPropertyRelative("_target").objectReferenceValue = buttonObject.GetComponent<Button>();
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            try
            {
                EasyUIDisplay display = prefab.GetComponent<EasyUIDisplay>();
                display.SetViewScript(typeof(TestView).FullName, string.Empty, string.Empty);
                TestView view = managerObject.GetComponent<EasyUIRuntimeHost>().Manager.Open<TestView>(prefab);

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

        [Test]
        public void ModalDisplay_UsesOneSharedBlockingBackground()
        {
            var managerObject = new GameObject("UI Manager", typeof(RectTransform), typeof(EasyUIRuntimeHost));
            EasyUIDisplay prefab = CreateDisplayPrefab("Modal", "modal-test");
            try
            {
                var serialized = new SerializedObject(prefab);
                serialized.FindProperty("_backgroundMode").enumValueIndex = (int)UIBackgroundMode.Modal;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EasyUIManager manager = managerObject.GetComponent<EasyUIRuntimeHost>().Manager;
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
