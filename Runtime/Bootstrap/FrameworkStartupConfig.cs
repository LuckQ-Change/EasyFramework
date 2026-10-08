using System;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>供场景启动器和自动启动器共同使用的框架启动配置。</summary>
    [CreateAssetMenu(
        fileName = ResourceName,
        menuName = "EasyFramework/Startup Config",
        order = 1)]
    public sealed class FrameworkStartupConfig : ScriptableObject
    {
        public const string ResourceName = "FrameworkStartupConfig";

        private static FrameworkStartupConfig _runtimeDefault;

        [SerializeField, HideInInspector] private string _startupProcedureTypeName;

        [Header("UI 启动")]
        [SerializeField] private bool _startUI = true;
        [SerializeField] private UIRenderMode _renderMode = UIRenderMode.ScreenSpaceCamera;
        [SerializeField] private Camera _uiCamera;
        [SerializeField] private bool _createCameraWhenMissing = true;
        [SerializeField] private bool _createEventSystem = true;

        [Header("UI 相机")]
        [SerializeField] private string _uiLayerName = "UI";
        [SerializeField] private float _cameraDepth = 100f;
        [SerializeField, Min(0.01f)] private float _planeDistance = 100f;

        [Header("屏幕适配（UI 与 Loading 共用）")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        [Header("UI 共享背景")]
        [SerializeField] private GameObject _backgroundPrefab;

        [Header("运行环境")]
        [SerializeField] private RuntimeMode _runtimeMode = RuntimeMode.Editor;

        [Header("全局 Loading")]
        [SerializeField] private LoadingSettings _loading = new LoadingSettings();

        public bool StartUI => _startUI;
        public UIRenderMode RenderMode => _renderMode;
        public Camera UICamera => _uiCamera;
        public bool CreateCameraWhenMissing => _createCameraWhenMissing;
        public bool CreateEventSystem => _createEventSystem;
        public string UILayerName => _uiLayerName;
        public float CameraDepth => _cameraDepth;
        public float PlaneDistance => _planeDistance;
        public Vector2 ReferenceResolution => _referenceResolution;
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        public GameObject BackgroundPrefab => _backgroundPrefab;
        public RuntimeMode RuntimeMode => _runtimeMode;
        public LoadingSettings Loading
        {
            get
            {
                if (_loading == null) _loading = new LoadingSettings();
                return _loading;
            }
        }

        /// <summary>配置的项目启动流程类型；未配置时返回 null。</summary>
        public Type StartupProcedureType
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_startupProcedureTypeName)) return null;
                Type type = Type.GetType(_startupProcedureTypeName, false);
                if (type == null)
                    throw new InvalidOperationException(
                        $"无法加载启动流程类型“{_startupProcedureTypeName}”，请在启动配置中重新选择脚本。");
                if (!typeof(ProcedureBase).IsAssignableFrom(type))
                    throw new InvalidOperationException(
                        $"启动流程类型“{type.FullName}”必须继承 ProcedureBase。");
                return type;
            }
        }

        /// <summary>按照当前配置创建或更新持久化 UI 根节点与全局 Loading。</summary>
        public UIRuntimeHost Apply()
        {
            UIRuntimeHost host = null;
            if (_startUI)
            {
                UIManager manager = UIManager.Instance;
                if (manager != null)
                {
                    host = manager.RuntimeHost;
                    host.Configure(
                        _renderMode,
                        _uiCamera,
                        _createCameraWhenMissing,
                        _createEventSystem,
                        _uiLayerName,
                        _cameraDepth,
                        _planeDistance,
                        _referenceResolution,
                        _matchWidthOrHeight,
                        _backgroundPrefab);
                }
            }

            LoadingRuntimeHost.Ensure(
                Loading,
                _uiLayerName,
                _referenceResolution,
                _matchWidthOrHeight);
            return host;
        }

        internal static FrameworkStartupConfig Resolve(FrameworkStartupConfig assigned)
        {
            if (assigned != null) return assigned;
            FrameworkStartupConfig resource = Resources.Load<FrameworkStartupConfig>(ResourceName);
            if (resource != null) return resource;
            if (_runtimeDefault != null) return _runtimeDefault;
            _runtimeDefault = CreateInstance<FrameworkStartupConfig>();
            _runtimeDefault.name = "内置 EasyFramework 启动配置";
            _runtimeDefault.hideFlags = HideFlags.HideAndDontSave;
            return _runtimeDefault;
        }

        internal static void ResetRuntimeDefault() => _runtimeDefault = null;

        private void OnValidate()
        {
            if (_referenceResolution.x < 1f) _referenceResolution.x = 1f;
            if (_referenceResolution.y < 1f) _referenceResolution.y = 1f;
            if (_planeDistance < 0.01f) _planeDistance = 0.01f;
            if (string.IsNullOrWhiteSpace(_uiLayerName)) _uiLayerName = "UI";
            if (_loading == null) _loading = new LoadingSettings();
        }
    }
}
