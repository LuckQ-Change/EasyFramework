using System;
using System.Collections;
using System.Reflection;
using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class UGUIComponentConverterTests
    {
        [Test]
        public void Converter_ReplacesNativeImage_AndRestoresObjectReferences()
        {
            var gameObject = new GameObject(
                "Convert Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            try
            {
                var nativeImage = gameObject.GetComponent<Image>();
                var button = gameObject.GetComponent<Button>();
                button.targetGraphic = nativeImage;

                Type converter = Type.GetType(
                    "EasyFramework.Editor.UI.UGUIComponentConverter, com.wjq.easyframework.editor");
                Assert.IsNotNull(converter);
                MethodInfo replace = converter.GetMethod("Replace", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(replace);

                bool converted = (bool)replace.Invoke(null, new object[] { nativeImage, typeof(UIImage) });
                var uiImage = gameObject.GetComponent<UIImage>();

                Assert.IsTrue(converted);
                Assert.IsNotNull(uiImage);
                Assert.AreEqual(typeof(UIImage), gameObject.GetComponent<Image>().GetType());
                Assert.AreSame(uiImage, button.targetGraphic);
                Assert.IsNull(
                    gameObject.GetComponent<UIElement>(),
                    "Switching Image to UIImage must not attach a state machine component.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Converter_OnlyOffersUIImageForForwardConversion()
        {
            Type converter = Type.GetType(
                "EasyFramework.Editor.UI.UGUIComponentConverter, com.wjq.easyframework.editor");
            Assert.IsNotNull(converter);
            FieldInfo mapField = converter.GetField("ToExtended", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(mapField);
            var map = (IDictionary)mapField.GetValue(null);

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual(typeof(UIImage), map[typeof(Image)]);
            Assert.IsFalse(map.Contains(typeof(Button)));
        }
    }
}
