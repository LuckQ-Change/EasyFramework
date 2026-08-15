using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    public enum EasyUIRenderMode
    {
        ScreenSpaceOverlay,
        ScreenSpaceCamera,
    }

    /// <summary>
    /// Minimal Unity adapter for the pure C# EasyUIManager service.
    /// </summary>
    [AddComponentMenu("EasyFramework/UI/Runtime Host")]
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster))]
    public sealed class EasyUIRuntimeHost : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;
        [SerializeField] private EasyUIRenderMode _renderMode = EasyUIRenderMode.ScreenSpaceOverlay;
        [SerializeField] private Camera _uiCamera;
        [SerializeField] private bool _createCameraWhenMissing;
        [SerializeField] private bool _createEventSystem;
        [SerializeField] private string _uiLayerName = "UI";
        [SerializeField] private float _cameraDepth = 100f;
        [SerializeField, Min(0.01f)] private float _planeDistance = 100f;

        [Header("Shared Background")]
        [SerializeField] private GameObject _backgroundPrefab;

        private EasyUIManager _manager;
        private readonly HashSet<IDisposable> _pendingDisposals = new HashSet<IDisposable>();

        public Vector2 ReferenceResolution => _referenceResolution;
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        public EasyUIRenderMode RenderMode => _renderMode;
        public Camera UICamera => _uiCamera;
        public float PlaneDistance => _planeDistance;
        public int UILayer => ResolveUILayer();
        public GameObject BackgroundPrefab => _backgroundPrefab;
        public EasyUIManager Manager
        {
            get
            {
                if (_manager != null) return _manager;
                EasyUIManager.TryAttachHost(this, out _manager);
                return _manager;
            }
        }

        private void Awake()
        {
            if (!EasyUIManager.TryAttachHost(this, out _manager))
            {
                DestroyUnityObject(gameObject);
                return;
            }
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnValidate() => _manager?.RefreshCanvasSettings(this);

        private void OnApplicationQuit() => EasyUIManager.NotifyApplicationQuit();

        private void OnDisable()
        {
            // EditMode destruction/scene changes disable components before OnDestroy.
            // Detach here so static manager state cannot leak into the next host.
            if (!Application.isPlaying) DetachManager();
        }

        private void OnDestroy()
        {
            foreach (IDisposable disposable in _pendingDisposals) disposable?.Dispose();
            _pendingDisposals.Clear();
            DetachManager();
        }

        internal void SetManager(EasyUIManager manager) => _manager = manager;

        /// <summary>Apply startup-preset settings to this persistent host.</summary>
        public void Configure(
            EasyUIRenderMode renderMode,
            Camera uiCamera,
            bool createCameraWhenMissing,
            bool createEventSystem,
            string uiLayerName,
            float cameraDepth,
            float planeDistance,
            Vector2 referenceResolution,
            float matchWidthOrHeight,
            GameObject backgroundPrefab = null)
        {
            _renderMode = renderMode;
            _uiCamera = uiCamera;
            _createCameraWhenMissing = createCameraWhenMissing;
            _createEventSystem = createEventSystem;
            _uiLayerName = string.IsNullOrWhiteSpace(uiLayerName) ? "UI" : uiLayerName;
            _cameraDepth = cameraDepth;
            _planeDistance = Mathf.Max(0.01f, planeDistance);
            _referenceResolution = new Vector2(
                Mathf.Max(1f, referenceResolution.x),
                Mathf.Max(1f, referenceResolution.y));
            _matchWidthOrHeight = Mathf.Clamp01(matchWidthOrHeight);
            _backgroundPrefab = backgroundPrefab;

            if (_renderMode == EasyUIRenderMode.ScreenSpaceCamera) EnsureUICamera();
            if (_createEventSystem) EnsureEventSystem();
            _manager?.RefreshCanvasSettings(this);
        }

        internal Camera ResolveUICamera()
        {
            if (_renderMode == EasyUIRenderMode.ScreenSpaceOverlay) return null;
            return EnsureUICamera();
        }

        private Camera EnsureUICamera()
        {
            if (_uiCamera != null && _uiCamera.gameObject.scene.IsValid())
                return ConfigureUICamera(_uiCamera);
            if (_uiCamera != null)
            {
                Camera prefab = _uiCamera;
                _uiCamera = Instantiate(prefab, transform);
                _uiCamera.name = "[EasyUI Camera]";
                return ConfigureUICamera(_uiCamera);
            }
            if (!_createCameraWhenMissing) return null;

            var cameraObject = new GameObject("[EasyUI Camera]", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            _uiCamera = cameraObject.GetComponent<Camera>();
            return ConfigureUICamera(_uiCamera);
        }

        private Camera ConfigureUICamera(Camera camera)
        {
            int uiLayer = ResolveUILayer();
            camera.gameObject.layer = uiLayer;
            camera.clearFlags = CameraClearFlags.Depth;
            camera.cullingMask = 1 << uiLayer;
            camera.orthographic = true;
            camera.depth = _cameraDepth;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            return camera;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>(true) != null) return;
            var eventObject = new GameObject(
                "[EasyUI EventSystem]",
                typeof(EasyEventSystem),
                typeof(EasyStandaloneInputModule));
            eventObject.transform.SetParent(transform, false);
        }

        private int ResolveUILayer()
        {
            int layer = LayerMask.NameToLayer(_uiLayerName);
            return layer < 0 ? 5 : layer;
        }

        private void DetachManager()
        {
            _manager = null;
            EasyUIManager.ReleaseHost(this);
        }

        internal void DisposeAfterFrame(IDisposable disposable)
        {
            if (disposable == null) return;
            if (isActiveAndEnabled)
            {
                _pendingDisposals.Add(disposable);
                StartCoroutine(DisposeNextFrame(disposable));
            }
            else disposable.Dispose();
        }

        private IEnumerator DisposeNextFrame(IDisposable disposable)
        {
            yield return null;
            if (_pendingDisposals.Remove(disposable)) disposable.Dispose();
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
