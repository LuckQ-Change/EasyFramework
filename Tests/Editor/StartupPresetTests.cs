using EasyFramework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class StartupPresetTests
    {
        [Test]
        public void Apply_CreatesCameraCanvasAndEventSystem()
        {
            var preset = ScriptableObject.CreateInstance<EasyFrameworkStartupPreset>();
            EasyUIRuntimeHost host = null;
            try
            {
                var serialized = new SerializedObject(preset);
                serialized.FindProperty("_startUI").boolValue = true;
                serialized.FindProperty("_renderMode").enumValueIndex = (int)EasyUIRenderMode.ScreenSpaceCamera;
                serialized.FindProperty("_createCameraWhenMissing").boolValue = true;
                serialized.FindProperty("_createEventSystem").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                host = preset.Apply();
                Canvas canvas = host.GetComponent<Canvas>();

                Assert.NotNull(host);
                Assert.NotNull(host.UICamera);
                Assert.NotNull(canvas);
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
                Assert.AreSame(host.UICamera, canvas.worldCamera);
                Assert.NotNull(Object.FindObjectOfType<EventSystem>(true));
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host.gameObject);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void Apply_CreatesIndependentOverlayLoadingCanvas()
        {
            var preset = ScriptableObject.CreateInstance<EasyFrameworkStartupPreset>();
            var loadingContent = new GameObject("Loading Content", typeof(RectTransform));
            EasyLoadingRuntimeHost loadingHost = null;
            try
            {
                var serialized = new SerializedObject(preset);
                serialized.FindProperty("_startUI").boolValue = false;
                SerializedProperty loading = serialized.FindProperty("_loading");
                loading.FindPropertyRelative("_prefab").objectReferenceValue = loadingContent;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EasyUIRuntimeHost uiHost = preset.Apply();
                loadingHost = Object.FindObjectOfType<EasyLoadingRuntimeHost>(true);

                Assert.IsNull(uiHost);
                Assert.NotNull(loadingHost);
                Assert.NotNull(loadingHost.Canvas);
                Assert.NotNull(loadingHost.GetComponent<CanvasScaler>());
                Assert.NotNull(loadingHost.GetComponent<GraphicRaycaster>());
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, loadingHost.Canvas.renderMode);
                Assert.IsNull(loadingHost.Canvas.worldCamera);
                Assert.AreEqual(10000, loadingHost.Canvas.sortingOrder);
            }
            finally
            {
                if (loadingHost != null) Object.DestroyImmediate(loadingHost.gameObject);
                Object.DestroyImmediate(loadingContent);
                Object.DestroyImmediate(preset);
            }
        }
    }
}
