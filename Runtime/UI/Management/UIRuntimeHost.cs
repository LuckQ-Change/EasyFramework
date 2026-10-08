using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    public enum UIRenderMode
    {
        ScreenSpaceOverlay,
        ScreenSpaceCamera,
    }

    /// <summary>连接纯 C# UIManager 服务与 Unity 生命周期的轻量适配器。</summary>
    [AddComponentMenu("EasyFramework/UI/Runtime Host")]
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster))]
    public sealed class UIRuntimeHost : MonoBehaviour
    {
        private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        private float _matchWidthOrHeight = 0.5f;
        private UIRenderMode _renderMode = UIRenderMode.ScreenSpaceOverlay;
        private Camera _uiCamera;
        private bool _ownsUICamera;
        private bool _createCameraWhenMissing;
        private bool _createEventSystem;
        private string _uiLayerName = "UI";
        private float _cameraDepth = 100f;
        private float _planeDistance = 100f;
        private GameObject _backgroundPrefab;

        private UIManager _manager;
        private readonly HashSet<IDisposable> _pendingDisposals = new HashSet<IDisposable>();

        public Vector2 ReferenceResolution => _referenceResolution;
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        public UIRenderMode RenderMode => _renderMode;
        public Camera UICamera => _uiCamera;
        public float PlaneDistance => _planeDistance;
        public int UILayer => ResolveUILayer();
        public GameObject BackgroundPrefab => _backgroundPrefab;
        public UIManager Manager
        {
            get
            {
                if (_manager != null) return _manager;
                UIManager.TryAttachHost(this, out _manager);
                return _manager;
            }
        }

        private void Awake()
        {
            if (!UIManager.TryAttachHost(this, out _manager))
            {
                DestroyUnityObject(gameObject);
                return;
            }
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private void OnValidate() => _manager?.RefreshCanvasSettings(this);

        private void OnApplicationQuit() => UIManager.NotifyApplicationQuit();

        private void OnDisable()
        {
            // 编辑模式销毁对象或切换场景时，组件会先禁用再销毁。
            // 在这里解除关联，避免静态 Manager 状态泄漏到下一个 Host。
            if (!Application.isPlaying) DetachManager();
        }

        private void OnDestroy()
        {
            foreach (IDisposable disposable in _pendingDisposals) disposable?.Dispose();
            _pendingDisposals.Clear();
            ReleaseOwnedUICamera();
            DetachManager();
        }

        internal void SetManager(UIManager manager) => _manager = manager;

        /// <summary>把启动配置应用到这个持久化 Host。</summary>
        public void Configure(
            UIRenderMode renderMode,
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
            SetConfiguredUICamera(uiCamera);
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

            if (_renderMode == UIRenderMode.ScreenSpaceCamera) EnsureUICamera();
            if (_createEventSystem) EnsureEventSystem();
            _manager?.RefreshCanvasSettings(this);
        }

        internal Camera ResolveUICamera()
        {
            if (_renderMode == UIRenderMode.ScreenSpaceOverlay) return null;
            return EnsureUICamera();
        }

        private Camera EnsureUICamera()
        {
            if (_uiCamera != null && _uiCamera.gameObject.scene.IsValid())
                return ConfigureUICamera(_uiCamera);
            if (_uiCamera != null)
            {
                Camera prefab = _uiCamera;
                _uiCamera = Instantiate(prefab);
                _uiCamera.name = "[UI Camera]";
                _ownsUICamera = true;
                KeepCameraAcrossScenes(_uiCamera);
                return ConfigureUICamera(_uiCamera);
            }
            if (!_createCameraWhenMissing) return null;

            var cameraObject = new GameObject("[UI Camera]", typeof(Camera));
            _uiCamera = cameraObject.GetComponent<Camera>();
            _ownsUICamera = true;
            KeepCameraAcrossScenes(_uiCamera);
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

        private void SetConfiguredUICamera(Camera configuredCamera)
        {
            if (!_ownsUICamera && _uiCamera == configuredCamera) return;
            ReleaseOwnedUICamera();
            _uiCamera = configuredCamera;
            _ownsUICamera = false;
        }

        private void ReleaseOwnedUICamera()
        {
            if (!_ownsUICamera) return;
            Camera ownedCamera = _uiCamera;
            _uiCamera = null;
            _ownsUICamera = false;
            if (ownedCamera != null) DestroyUnityObject(ownedCamera.gameObject);
        }

        private static void KeepCameraAcrossScenes(Camera camera)
        {
            if (camera == null) return;
            camera.transform.SetParent(null, true);
            if (Application.isPlaying) DontDestroyOnLoad(camera.gameObject);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>(true) != null) return;
            var eventObject = new GameObject(
                "[UI EventSystem]",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventObject.transform.SetParent(transform, false);
        }

        private int ResolveUILayer()
        {
            int layer = LayerMask.NameToLayer(_uiLayerName);
            if (layer >= 0) return layer;

            int fallback = LayerMask.NameToLayer("UI");
            Log.Warn($"[UI] Layer '{_uiLayerName}' 不存在，已回退到 '{(fallback >= 0 ? "UI" : "Default")}'。");
            return fallback >= 0 ? fallback : 0;
        }

        private void DetachManager()
        {
            _manager = null;
            UIManager.ReleaseHost(this);
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
