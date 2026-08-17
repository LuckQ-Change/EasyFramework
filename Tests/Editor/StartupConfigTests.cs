using EasyFramework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyFramework.Tests
{
    public sealed class ConfigStartupProcedure : ProcedureBase
    {
        public static int EnterCount;

        protected override System.Threading.Tasks.Task<System.Type> OnEnterAsync(
            System.Threading.CancellationToken cancellationToken)
        {
            EnterCount++;
            return Stay();
        }
    }

    public sealed class StartupConfigTests
    {
        [Test]
        public void StartupProcedureType_ResolvesAndStartsProjectProcedure()
        {
            var config = ScriptableObject.CreateInstance<EasyFrameworkStartupConfig>();
            var modules = new ModuleManager();
            try
            {
                var serialized = new SerializedObject(config);
                System.Type expected = typeof(ConfigStartupProcedure);
                serialized.FindProperty("_startupProcedureTypeName").stringValue =
                    $"{expected.FullName}, {expected.Assembly.GetName().Name}";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(expected, config.StartupProcedureType);

                ProcedureModule procedures = modules.Register<ProcedureModule>();
                modules.InitAll();
                ConfigStartupProcedure.EnterCount = 0;
                procedures.StartAsync(config.StartupProcedureType, new ProcedureContext())
                    .GetAwaiter().GetResult();

                Assert.AreEqual(1, ConfigStartupProcedure.EnterCount);
                Assert.AreEqual(expected, procedures.ActiveProcedureType);
            }
            finally
            {
                modules.ShutdownAll();
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Apply_WithEmptyLoadingPrefab_DoesNotCreateLoadingHost()
        {
            var config = ScriptableObject.CreateInstance<EasyFrameworkStartupConfig>();
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("_startUI").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsNull(config.Apply());
                Assert.IsNull(Object.FindObjectOfType<EasyLoadingRuntimeHost>(true));
            }
            finally
            {
                EasyLoadingRuntimeHost loadingHost = Object.FindObjectOfType<EasyLoadingRuntimeHost>(true);
                if (loadingHost != null) Object.DestroyImmediate(loadingHost.gameObject);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Apply_CreatesCameraCanvasAndEventSystem()
        {
            var config = ScriptableObject.CreateInstance<EasyFrameworkStartupConfig>();
            EasyUIRuntimeHost host = null;
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("_startUI").boolValue = true;
                serialized.FindProperty("_renderMode").enumValueIndex = (int)EasyUIRenderMode.ScreenSpaceCamera;
                serialized.FindProperty("_createCameraWhenMissing").boolValue = true;
                serialized.FindProperty("_createEventSystem").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                host = config.Apply();
                Canvas canvas = host.GetComponent<Canvas>();

                Assert.NotNull(host);
                Assert.NotNull(host.UICamera);
                Assert.IsNull(host.UICamera.transform.parent);
                Assert.NotNull(canvas);
                Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
                Assert.AreSame(host.UICamera, canvas.worldCamera);
                Assert.NotNull(Object.FindObjectOfType<EventSystem>(true));
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host.gameObject);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Apply_CreatesIndependentOverlayLoadingCanvas()
        {
            var config = ScriptableObject.CreateInstance<EasyFrameworkStartupConfig>();
            var loadingContent = new GameObject("Loading Content", typeof(RectTransform));
            EasyLoadingRuntimeHost loadingHost = null;
            try
            {
                var serialized = new SerializedObject(config);
                serialized.FindProperty("_startUI").boolValue = false;
                serialized.FindProperty("_referenceResolution").vector2Value = new Vector2(1600f, 900f);
                serialized.FindProperty("_matchWidthOrHeight").floatValue = 0.25f;
                SerializedProperty loading = serialized.FindProperty("_loading");
                loading.FindPropertyRelative("_prefab").objectReferenceValue = loadingContent;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EasyUIRuntimeHost uiHost = config.Apply();
                loadingHost = Object.FindObjectOfType<EasyLoadingRuntimeHost>(true);

                Assert.IsNull(uiHost);
                Assert.NotNull(loadingHost);
                Assert.NotNull(loadingHost.Canvas);
                Assert.NotNull(loadingHost.GetComponent<CanvasScaler>());
                Assert.NotNull(loadingHost.GetComponent<GraphicRaycaster>());
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, loadingHost.Canvas.renderMode);
                Assert.IsNull(loadingHost.Canvas.worldCamera);
                Assert.AreEqual(10000, loadingHost.Canvas.sortingOrder);
                CanvasScaler scaler = loadingHost.GetComponent<CanvasScaler>();
                Assert.AreEqual(new Vector2(1600f, 900f), scaler.referenceResolution);
                Assert.AreEqual(0.25f, scaler.matchWidthOrHeight);
            }
            finally
            {
                if (loadingHost != null) Object.DestroyImmediate(loadingHost.gameObject);
                Object.DestroyImmediate(loadingContent);
                Object.DestroyImmediate(config);
            }
        }
    }
}
