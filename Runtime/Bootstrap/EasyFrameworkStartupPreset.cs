using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>Startup configuration used by both  launchers and the automatic launcher.</summary>
    [CreateAssetMenu(
        fileName = ResourceName,
        menuName = "EasyFramework/Startup Preset",
        order = 1)]
    public sealed class EasyFrameworkStartupPreset : ScriptableObject
    {
        public const string ResourceName = "EasyFrameworkStartupPreset";

        private static EasyFrameworkStartupPreset _runtimeDefault;

        [Header("UI Startup")]
        [SerializeField] private bool _startUI = true;
        [SerializeField] private EasyUIRenderMode _renderMode = EasyUIRenderMode.ScreenSpaceCamera;
        [SerializeField] private Camera _uiCamera;
        [SerializeField] private bool _createCameraWhenMissing = true;
        [SerializeField] private bool _createEventSystem = true;

        [Header("UI Camera")]
        [SerializeField] private string _uiLayerName = "UI";
        [SerializeField] private float _cameraDepth = 100f;
        [SerializeField, Min(0.01f)] private float _planeDistance = 100f;

        [Header("Canvas Scaler")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        [Header("Shared Background")]
        [SerializeField] private GameObject _backgroundPrefab;

        [Header("Procedure Flow")]
        [SerializeField] private bool _startProcedureFlow = true;
        [SerializeField] private EasyRuntimeMode _runtimeMode = EasyRuntimeMode.Editor;

        [Header("Loading Display")]
        [SerializeField] private EasyLoadingSettings _loading = new EasyLoadingSettings();

        [Header("Hot Update")]
        [SerializeField] private string[] _aotMetadataLocations = Array.Empty<string>();
        [SerializeField] private string _hotfixAssemblyLocation;
        [SerializeField] private string _hotfixEntryType;
        [SerializeField] private string _hotfixEntryMethod = "Start";

        [Header("UI Preload")]
        [SerializeField] private string[] _preloadUILocations = Array.Empty<string>();

        [Header("Login / Game / Exit")]
        [SerializeField] private string _loginViewLocation;
        [SerializeField] private string _gameViewLocation;
        [SerializeField] private bool _quitApplicationOnExit = true;

        public bool StartUI => _startUI;
        public EasyUIRenderMode RenderMode => _renderMode;
        public Camera UICamera => _uiCamera;
        public bool CreateCameraWhenMissing => _createCameraWhenMissing;
        public bool CreateEventSystem => _createEventSystem;
        public string UILayerName => _uiLayerName;
        public float CameraDepth => _cameraDepth;
        public float PlaneDistance => _planeDistance;
        public Vector2 ReferenceResolution => _referenceResolution;
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        public GameObject BackgroundPrefab => _backgroundPrefab;
        public bool StartProcedureFlow => _startProcedureFlow;
        public EasyRuntimeMode RuntimeMode => _runtimeMode;
        public EasyLoadingSettings Loading
        {
            get
            {
                if (_loading == null) _loading = new EasyLoadingSettings();
                return _loading;
            }
        }
        public IReadOnlyList<string> AotMetadataLocations =>
            _aotMetadataLocations ?? Array.Empty<string>();
        public string HotfixAssemblyLocation => _hotfixAssemblyLocation;
        public string HotfixEntryType => _hotfixEntryType;
        public string HotfixEntryMethod => _hotfixEntryMethod;
        public IReadOnlyList<string> PreloadUILocations =>
            _preloadUILocations ?? Array.Empty<string>();
        public string LoginViewLocation => _loginViewLocation;
        public string GameViewLocation => _gameViewLocation;
        public bool QuitApplicationOnExit => _quitApplicationOnExit;
        public bool HasHotUpdateConfiguration =>
            (_aotMetadataLocations?.Length ?? 0) > 0 || !string.IsNullOrWhiteSpace(_hotfixAssemblyLocation);
        public bool ShouldRunHotUpdate => _runtimeMode == EasyRuntimeMode.Online && HasHotUpdateConfiguration;
        public bool RequiresAssets =>
            ShouldRunHotUpdate || (_preloadUILocations?.Length ?? 0) > 0 ||
            !string.IsNullOrWhiteSpace(_loginViewLocation) || !string.IsNullOrWhiteSpace(_gameViewLocation);

        /// <summary>Create/configure the persistent UI root described by this preset.</summary>
        public EasyUIRuntimeHost Apply()
        {
            EasyLoadingRuntimeHost.Ensure(Loading);
            if (!_startUI) return null;
            EasyUIManager manager = EasyUIManager.Instance;
            if (manager == null) return null;
            EasyUIRuntimeHost host = manager.RuntimeHost;
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
            return host;
        }

        /// <summary>Start the built-in startup-to-login procedure chain.</summary>
        public Task StartFlowAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ProcedureModule procedures = ProcedureModule.Instance;
            if (procedures == null)
                throw new InvalidOperationException("ProcedureModule is not registered.");
            return procedures.StartAsync<StartupProcedure>(new ProcedureContext(this), cancellationToken);
        }

        internal static EasyFrameworkStartupPreset Resolve(EasyFrameworkStartupPreset assigned)
        {
            if (assigned != null) return assigned;
            EasyFrameworkStartupPreset resource = Resources.Load<EasyFrameworkStartupPreset>(ResourceName);
            if (resource != null) return resource;
            if (_runtimeDefault != null) return _runtimeDefault;
            _runtimeDefault = CreateInstance<EasyFrameworkStartupPreset>();
            _runtimeDefault.name = "Built-in EasyFramework Startup Preset";
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
            if (_loading == null) _loading = new EasyLoadingSettings();
            if (_aotMetadataLocations == null) _aotMetadataLocations = Array.Empty<string>();
            if (_preloadUILocations == null) _preloadUILocations = Array.Empty<string>();
        }
    }
}
