using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class UIBindingTests
    {
        private sealed class LevelModel
        {
            public ReactiveProperty<int> Level { get; } = new ReactiveProperty<int>(1);
            [UIBindable("Title")]
            private ReactiveProperty<string> InternalTitle { get; } = new ReactiveProperty<string>("关卡");
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
        public void EasyImage_CanSwitchSpriteByIndexWithoutStateController()
        {
            var root = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(EasyImage));
            var image = root.GetComponent<EasyImage>();
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
    }
}
