using System;
using System.Reflection;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.Tests
{
    public sealed class UIBindingTests
    {
        [Test]
        public void ReusedItems_KeepReferenceKeysInTheirOwnScope()
        {
            var view = new GameObject("View", typeof(RectTransform), typeof(UIDisplay), typeof(UIReference));
            try
            {
                SetReference(view.GetComponent<UIReference>(), "Title", view.GetComponent<RectTransform>());
                for (int i = 0; i < 2; i++)
                {
                    var item = new GameObject("Item" + i, typeof(RectTransform),
                        typeof(UIDisplay), typeof(UIReference));
                    item.transform.SetParent(view.transform, false);
                    var displayData = new SerializedObject(item.GetComponent<UIDisplay>());
                    displayData.FindProperty("_managedAsView").boolValue = false;
                    displayData.ApplyModifiedPropertiesWithoutUndo();
                    SetReference(item.GetComponent<UIReference>(), "Title", item.GetComponent<RectTransform>());
                    Assert.AreSame(item.GetComponent<RectTransform>(),
                        item.GetComponent<UIDisplay>().Binding.Get<RectTransform>("Title"));
                }
                Assert.AreSame(view.GetComponent<RectTransform>(),
                    view.GetComponent<UIDisplay>().Binding.Get<RectTransform>("Title"));
            }
            finally
            {
                Object.DestroyImmediate(view);
            }
        }

        private static void SetReference(UIReference marker, string key, Component target)
        {
            var serialized = new SerializedObject(marker);
            var entries = serialized.FindProperty("_entries");
            entries.arraySize = 1;
            var entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("_key").stringValue = key;
            entry.FindPropertyRelative("_target").objectReferenceValue = target;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public sealed class EditorBindingView : UIView
        {
            public ReactiveProperty<int> Level { get; } = new ReactiveProperty<int>(1);
        }

        private sealed class LevelModel
        {
            public ReactiveProperty<int> Level { get; } = new ReactiveProperty<int>(1);
            [UIBindable("Title")]
            private ReactiveProperty<string> InternalTitle { get; } = new ReactiveProperty<string>("关卡");
        }

        [Test]
        public void ReferenceAction_ExposesSelectedImageWithUniqueKey()
        {
            var root = new GameObject("View", typeof(RectTransform), typeof(UIDisplay));
            var child = new GameObject("StatusPanel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(root.transform, false);
            try
            {
                Type action = Type.GetType(
                    "EasyFramework.Editor.UI.UIReferenceEditorActions, com.wjq.easyframework.editor");
                Assert.NotNull(action);
                MethodInfo expose = action.GetMethod("Expose",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(expose);
                var display = root.GetComponent<UIDisplay>();
                var first = (UIReference)expose.Invoke(null, new object[] { display, child });
                var second = (UIReference)expose.Invoke(null, new object[] { display, child });
                Assert.AreSame(first, second);
                Assert.AreEqual(1, first.Entries.Count);
                Assert.AreEqual("StatusPanelImage", first.Entries[0].Key);
                Assert.AreSame(child.GetComponent<Image>(), first.Entries[0].Target);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BindingContext_RefreshesTextWhenModelChanges()
        {
            var root = new GameObject("Root");
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(root.transform);
            var label = labelObject.GetComponent<Text>();
            var context = root.AddComponent<UIBindingContext>();
            var model = new LevelModel();

            context.SetSource(model);
            context.RegisterBinding(label, UIBindingProperty.Text, "Level", "关卡 {0}");
            Assert.AreEqual("关卡 1", label.text);

            model.Level.Value = 2;
            Assert.AreEqual("关卡 2", label.text);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void BindingSource_PrivateAliasedProperty_IsAutomaticallyRegistered()
        {
            var root = new GameObject("Root");
            var input = root.AddComponent<InputField>();
            var context = root.AddComponent<UIBindingContext>();
            var model = new LevelModel();

            context.SetSource(model);
            context.RegisterBinding(input, UIBindingProperty.Text, "Title");

            Assert.AreEqual("关卡", input.text);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void UIImage_CanSwitchSpriteByIndexWithoutStateController()
        {
            var root = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIImage));
            var image = root.GetComponent<UIImage>();
            var texture = new Texture2D(4, 2);
            var first = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            var second = Sprite.Create(texture, new Rect(2, 0, 2, 2), Vector2.zero);

            image.SetSprites(new[] { first, second });
            Assert.IsTrue(image.SetSpriteIndex(1));
            Assert.AreEqual(1, image.SpriteIndex);
            Assert.AreSame(second, image.sprite);

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void EditorReflection_ResolvesAssemblyQualifiedManagedViewType()
        {
            var root = new GameObject("Binding View", typeof(UIDisplay), typeof(UIBindingContext));
            try
            {
                root.GetComponent<UIDisplay>().SetViewScript(
                    typeof(EditorBindingView).AssemblyQualifiedName,
                    string.Empty,
                    string.Empty);
                Type reflectionType = Type.GetType(
                    "EasyFramework.Editor.UI.UIBindingEditorReflection, com.wjq.easyframework.editor");
                Assert.IsNotNull(reflectionType);
                MethodInfo resolve = reflectionType.GetMethod(
                    "ResolveSourceType",
                    BindingFlags.Static | BindingFlags.Public);
                Assert.IsNotNull(resolve);

                Type resolved = (Type)resolve.Invoke(
                    null,
                    new object[] { root.GetComponent<UIBindingContext>() });

                Assert.AreEqual(typeof(EditorBindingView), resolved);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
