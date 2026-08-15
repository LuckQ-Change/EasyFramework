using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework
{
    /// <summary>Owns the overlay Canvas used only by the global loading display.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    public sealed class EasyLoadingRuntimeHost : MonoBehaviour
    {
        private EasyLoadingSettings _settings;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GameObject _instance;
        private LoadingModule _subscribedModule;

        public Canvas Canvas => _canvas != null ? _canvas : GetComponent<Canvas>();

        public static EasyLoadingRuntimeHost Ensure(EasyLoadingSettings settings)
        {
            if (settings == null || settings.Prefab == null) return null;

            EasyLoadingRuntimeHost host = FindObjectOfType<EasyLoadingRuntimeHost>(true);
            if (host == null)
            {
                var root = new GameObject(
                    "[EasyLoading]",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster),
                    typeof(EasyLoadingRuntimeHost));
                host = root.GetComponent<EasyLoadingRuntimeHost>();
                if (Application.isPlaying) DontDestroyOnLoad(root);
            }

            if (!host.gameObject.activeSelf) host.gameObject.SetActive(true);
            if (!host.enabled) host.enabled = true;
            host.Configure(settings);
            return host;
        }

        public void Configure(EasyLoadingSettings settings)
        {
            if (settings == null) throw new System.ArgumentNullException(nameof(settings));

            if (_settings != null && _settings.Prefab != settings.Prefab)
            {
                DestroyUnityObject(_instance);
                _instance = null;
            }

            _settings = settings;
            _canvas = GetComponent<Canvas>();
            _scaler = GetComponent<CanvasScaler>();
            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();

            _canvas.enabled = true;
            _scaler.enabled = true;
            raycaster.enabled = true;

            int layer = LayerMask.NameToLayer(settings.LayerName);
            if (layer < 0) layer = 5;
            gameObject.layer = layer;

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = settings.SortingOrder;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = settings.ReferenceResolution;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = settings.MatchWidthOrHeight;

            // Startup Loading is always an overlay. This avoids stale preset camera settings,
            // camera culling masks and render-pipeline camera composition entirely.
            RemoveLegacyCamera();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = null;

            EnsureSubscription();
            Apply(_subscribedModule != null ? _subscribedModule.State : default(LoadingState));
        }

        private void OnEnable()
        {
            EnsureSubscription();
            if (_subscribedModule != null) Apply(_subscribedModule.State);
        }

        private void OnDisable() => RemoveSubscription();

        private void OnDestroy()
        {
            RemoveSubscription();
        }

        private void EnsureSubscription()
        {
            LoadingModule module = LoadingModule.Instance;
            if (ReferenceEquals(module, _subscribedModule)) return;
            RemoveSubscription();
            _subscribedModule = module;
            if (_subscribedModule != null) _subscribedModule.Changed += Apply;
        }

        private void RemoveSubscription()
        {
            if (_subscribedModule != null) _subscribedModule.Changed -= Apply;
            _subscribedModule = null;
        }

        private void Apply(LoadingState state)
        {
            if (_settings == null || _settings.Prefab == null) return;

            if (state.IsLoading) Show();
            else Hide();
        }

        private void Show()
        {
            if (_instance == null)
            {
                _instance = Instantiate(_settings.Prefab, transform, false);
                _instance.name = _settings.Prefab.name;
                StretchToCanvas(_instance.transform as RectTransform);
                SetLayerRecursively(_instance.transform, gameObject.layer);
            }

            if (!_instance.activeSelf) _instance.SetActive(true);
            transform.SetAsLastSibling();
            UnityEngine.Canvas.ForceUpdateCanvases();
        }

        private void Hide()
        {
            if (_instance != null && _instance.activeSelf) _instance.SetActive(false);
        }

        private void RemoveLegacyCamera()
        {
            Transform legacyCamera = transform.Find("[EasyLoading Camera]");
            if (legacyCamera != null) DestroyUnityObject(legacyCamera.gameObject);
        }

        private static void StretchToCanvas(RectTransform rectTransform)
        {
            if (rectTransform == null) return;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
