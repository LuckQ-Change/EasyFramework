using EasyFramework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class UIStateTests
    {
        [Test]
        public void SetState_UpdatesSelectedIndex_AndAppliesChild()
        {
            var root = new GameObject("State Root", typeof(RectTransform), typeof(EasyUIStateController));
            var child = new GameObject(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(EasyUIElement));
            child.transform.SetParent(root.transform, false);
            try
            {
                var controller = root.GetComponent<EasyUIStateController>();
                var image = child.GetComponent<Image>();
                var element = child.GetComponent<EasyUIElement>();
                element.Controller = controller;
                image.color = Color.white;
                element.CaptureDefault();
                image.color = Color.red;
                element.CaptureVariant("Disabled");
                element.ApplyDefaultOnly();

                Assert.IsTrue(controller.SetState("Disabled"));
                Assert.AreEqual(1, controller.SelectedIndex);
                Assert.AreEqual("Disabled", controller.SelectedState);
                Assert.AreEqual(Color.red, image.color);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AddState_CanDefineMultipleCustomStates()
        {
            var root = new GameObject("State Root", typeof(EasyUIStateController));
            try
            {
                var controller = root.GetComponent<EasyUIStateController>();
                Assert.IsTrue(controller.AddState("Hover"));
                Assert.IsTrue(controller.AddState("Pressed"));
                Assert.IsTrue(controller.AddState());
                Assert.AreEqual(5, controller.States.Count);
                Assert.AreEqual("Hover", controller.States[2]);
                Assert.AreEqual("Pressed", controller.States[3]);
                Assert.AreEqual("State", controller.States[4]);
                Assert.IsFalse(controller.AddState("Hover"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RenameState_KeepsRecordedVariant()
        {
            var root = new GameObject("State Root", typeof(RectTransform), typeof(EasyUIStateController));
            var child = new GameObject(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(EasyUIElement));
            child.transform.SetParent(root.transform, false);
            try
            {
                var controller = root.GetComponent<EasyUIStateController>();
                var image = child.GetComponent<Image>();
                var element = child.GetComponent<EasyUIElement>();
                element.Controller = controller;
                image.color = Color.white;
                element.CaptureDefault();
                image.color = Color.red;
                element.CaptureVariant("Disabled");

                Assert.IsTrue(controller.RenameState(1, "Locked"));
                Assert.IsNull(element.FindVariant("Disabled"));
                Assert.IsNotNull(element.FindVariant("Locked"));

                element.ApplyDefaultOnly();
                Assert.IsTrue(controller.SetState("Locked"));
                Assert.AreEqual(Color.red, image.color);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Element_SyncsVariantsToControllerStates()
        {
            var root = new GameObject("State Root", typeof(EasyUIStateController));
            var child = new GameObject("Child", typeof(RectTransform), typeof(EasyUIElement));
            child.transform.SetParent(root.transform);
            try
            {
                var element = child.GetComponent<EasyUIElement>();
                element.Controller = root.GetComponent<EasyUIStateController>();
                Assert.AreEqual(2, element.Variants.Count);
                Assert.AreEqual("Normal", element.Variants[0].State);
                Assert.AreEqual("Disabled", element.Variants[1].State);
                Assert.AreEqual(UIStateProperty.None, element.Variants[0].Properties);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplyState_UsesCapturedVariant_AndDefaultRestore()
        {
            var root = new GameObject("State Root", typeof(RectTransform), typeof(EasyUIStateController));
            var child = new GameObject(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(EasyUIElement));
            child.transform.SetParent(root.transform, false);
            try
            {
                var controller = root.GetComponent<EasyUIStateController>();
                var image = child.GetComponent<Image>();
                var element = child.GetComponent<EasyUIElement>();
                element.Controller = controller;
                image.color = Color.white;
                element.CaptureDefault();

                image.color = Color.red;
                Assert.IsTrue(element.CaptureVariant("Disabled"));
                element.ApplyDefaultOnly();
                Assert.AreEqual(Color.white, image.color);

                element.ApplyState("Disabled");
                Assert.AreEqual(Color.red, image.color);

                controller.RestoreSerializedAppearance();
                Assert.AreEqual(Color.white, image.color);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NativeImage_CanJoinStateMachine_WithoutEasyImage()
        {
            var root = new GameObject("State Root", typeof(RectTransform), typeof(EasyUIStateController));
            var child = new GameObject(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(EasyUIElement));
            child.transform.SetParent(root.transform, false);
            try
            {
                Assert.IsNull(child.GetComponent<EasyImage>());
                var image = child.GetComponent<Image>();
                var element = child.GetComponent<EasyUIElement>();
                image.color = Color.green;
                element.CaptureDefault();
                image.color = Color.blue;
                element.CaptureVariant("Disabled");
                element.ApplyState("Disabled");
                Assert.AreEqual(Color.blue, image.color);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
