using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.UI
{
    /// <summary>纯 C# UI 服务，Unity 生命周期由 UIRuntimeHost 承接。</summary>
    public sealed class UIManager
    {
        private static UIManager _instance;
        private static bool _quitting;

        private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
        private readonly Dictionary<string, UIDisplay> _singletons =
            new Dictionary<string, UIDisplay>(StringComparer.Ordinal);
        private readonly Dictionary<UIDisplay, IDisposable> _assetLeases =
            new Dictionary<UIDisplay, IDisposable>();
        private readonly Dictionary<string, AssetLease<GameObject>> _preloaded =
            new Dictionary<string, AssetLease<GameObject>>(StringComparer.Ordinal);
        private readonly List<UIDisplay> _openOrder = new List<UIDisplay>();
        private readonly ReadOnlyCollection<UIDisplay> _openDisplays;

        private UIRuntimeHost _host;
        private UIBackground _background;
        private bool _shuttingDown;

        private UIManager()
        {
            _openDisplays = _openOrder.AsReadOnly();
        }

        public static UIManager Instance
        {
            get
            {
                if (_quitting) return null;
                if (_instance == null) _instance = new UIManager();
                _instance.EnsureHost();
                return _instance;
            }
        }

        /// <summary>返回已存在的管理器；不会因为查询而创建 UI 根节点。</summary>
        public static UIManager Current => _instance;

        public UIRuntimeHost RuntimeHost => _host;
        public int OpenCount
        {
            get
            {
                PruneDestroyedDisplays();
                return _openOrder.Count;
            }
        }
        public int PreloadedCount => _preloaded.Count;
        public IReadOnlyList<UIDisplay> OpenDisplays
        {
            get
            {
                PruneDestroyedDisplays();
                return _openDisplays;
            }
        }
        public UIDisplay TopDisplay => FindTopVisible();
        public UIView TopView => TopDisplay?.View;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _quitting = false;
        }

        internal static UIManager UseHost(UIRuntimeHost host)
        {
            if (host == null || _quitting) return null;
            if (_instance == null) _instance = new UIManager();
            if (_instance._host == null) _instance.AttachHost(host);
            return _instance;
        }

        internal static bool TryAttachHost(UIRuntimeHost host, out UIManager manager)
        {
            manager = UseHost(host);
            if (manager != null && manager._host == host) return true;
            manager = null;
            return false;
        }

        internal static void NotifyApplicationQuit() => _quitting = true;

        public RectTransform GetLayerRoot(UILayer layer)
        {
            EnsureHost();
            if (_host == null) return null;
            if (_layerRoots.Count == 0) BuildLayerRoots();
            return _layerRoots.TryGetValue(layer, out var root) && root != null ? root : CreateLayerRoot(layer);
        }

        public async Task<bool> PreloadAsync(string location)
        {
            ValidateLocation(location);
            if (_preloaded.TryGetValue(location, out var current) && current != null && current.IsValid) return true;
            AssetLease<GameObject> lease = await RequireAssets().AcquireAsync<GameObject>(location);
            if (lease == null || lease.Asset == null) return false;
            if (_shuttingDown || _quitting)
            {
                lease.Dispose();
                return false;
            }
            if (_preloaded.TryGetValue(location, out current) && current != null && current.IsValid)
            {
                lease.Dispose();
                return true;
            }
            current?.Dispose();
            _preloaded[location] = lease;
            return true;
        }

        public bool IsPreloaded(string location) =>
            !string.IsNullOrWhiteSpace(location) && _preloaded.TryGetValue(location, out var lease) &&
            lease != null && lease.IsValid;

        public bool ReleasePreloaded(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || !_preloaded.TryGetValue(location, out var lease)) return false;
            _preloaded.Remove(location);
            lease?.Dispose();
            return true;
        }

        public void ReleaseAllPreloaded()
        {
            foreach (var lease in _preloaded.Values) lease?.Dispose();
            _preloaded.Clear();
        }

        public UIView Open(GameObject prefab, object args = null, UILayer? layer = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            EnsureAvailable();
            return OpenPrefab(prefab, args, layer, out _).View;
        }

        public T Open<T>(GameObject prefab, object args = null, UILayer? layer = null) where T : UIView
        {
            UIView view = Open(prefab, args, layer);
            if (view is T typed) return typed;
            throw new InvalidOperationException($"UI prefab '{prefab.name}' created {view?.GetType().Name}, expected {typeof(T).Name}.");
        }

        public UIView OpenInstance(UIDisplay display, object args = null, UILayer? layer = null)
        {
            if (display == null) throw new ArgumentNullException(nameof(display));
            EnsureAvailable();
            UILayer targetLayer = layer ?? display.DefaultLayer;
            string key = ResolveKey(display, display.gameObject.name);
            if (display.SingleInstance && TryGetOpen(key, out var existing) && existing != display)
                return ActivateExisting(existing, args, targetLayer).View;
            RegisterOpenedDisplay(display, key, targetLayer, args);
            return display.View;
        }

        public async Task<T> OpenAsync<T>(
            string location,
            object args = null,
            UILayer? layer = null,
            CancellationToken cancellationToken = default(CancellationToken))
            where T : UIView
        {
            ValidateLocation(location);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAvailable();
            AssetLease<GameObject> lease = await RequireAssets().AcquireAsync<GameObject>(location);
            if (lease == null || lease.Asset == null) return null;
            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_shuttingDown || _quitting || _host == null)
            {
                lease.Dispose();
                return null;
            }
            try
            {
                UIDisplay opened = OpenPrefab(lease.Asset, args, layer, out bool created);
                if (!(opened.View is T typed))
                {
                    if (created) Close(opened);
                    throw new InvalidOperationException(
                        $"UI prefab '{location}' created {opened.View?.GetType().Name}, expected {typeof(T).Name}.");
                }
                if (!created) lease.Dispose();
                else _assetLeases[opened] = lease;
                return typed;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        /// <summary>使用 TView 上记录的 Prefab 地址打开界面。</summary>
        public Task<TView> OpenAsync<TView>(UILayer? layer = null) where TView : UIView =>
            OpenGeneratedAsync<TView>(null, layer);

        public Task<TView> OpenAsync<TView, TArgs>(TArgs args, UILayer? layer = null)
            where TView : UIView => OpenGeneratedAsync<TView>(args, layer);

        public Task<UIView> OpenAsync(string location, object args = null, UILayer? layer = null) =>
            OpenAsync<UIView>(location, args, layer);

        public async Task<TView> OpenGeneratedAsync<TView>(
            object args = null,
            UILayer? layer = null,
            CancellationToken cancellationToken = default(CancellationToken))
            where TView : UIView
        {
            if (!UIFactory.TryGetPrefabLocation<TView>(out string location))
                throw new InvalidOperationException(
                    $"No generated prefab location is registered for {typeof(TView).FullName}. Regenerate its binding script.");
            return await OpenAsync<TView>(location, args, layer, cancellationToken);
        }

        public bool Close(UIView view) => view != null && Close(view.Display);

        public bool Close(UIDisplay display)
        {
            if (display == null || !_openOrder.Remove(display)) return false;
            RemoveSingleton(display);
            display.CloseInternal();
            FocusTop();
            RefreshBackground();

            void DestroyClosedDisplay()
            {
                display.gameObject.SetActive(false);
                ReleaseDisplayLease(display);
                display.DisposeContent();
                DestroyUnityObject(display.gameObject);
            }

            var transition = display.GetComponent<UITransition>();
            if (Application.isPlaying && transition != null) transition.PlayClose(DestroyClosedDisplay);
            else DestroyClosedDisplay();
            return true;
        }

        public bool Close(string viewId)
        {
            if (string.IsNullOrWhiteSpace(viewId)) return false;
            if (_singletons.TryGetValue(viewId, out var exact)) return Close(exact);
            for (int i = _openOrder.Count - 1; i >= 0; i--)
            {
                UIDisplay display = _openOrder[i];
                if (display != null && (display.ViewId == viewId || display.name == viewId)) return Close(display);
            }
            return false;
        }

        public bool Close<T>() where T : UIView
        {
            T view = Get<T>();
            return view != null && Close(view);
        }

        public bool Back()
        {
            UIDisplay top = FindTopVisible(true);
            return top != null && Close(top);
        }

        public bool CloseTop()
        {
            UIDisplay top = FindTopVisible();
            return top != null && Close(top);
        }

        public void CloseLayer(UILayer layer)
        {
            var snapshot = _openOrder.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
                if (snapshot[i] != null && snapshot[i].CurrentLayer == layer) Close(snapshot[i]);
        }

        public void CloseAll()
        {
            var snapshot = _openOrder.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
                if (snapshot[i] != null) Close(snapshot[i]);
        }

        public T Get<T>() where T : UIView
        {
            for (int i = _openOrder.Count - 1; i >= 0; i--)
                if (_openOrder[i] != null && _openOrder[i].View is T typed) return typed;
            return null;
        }

        public bool TryGet<T>(out T view) where T : UIView
        {
            view = Get<T>();
            return view != null;
        }

        public bool TryGet(string viewId, out UIView view)
        {
            if (_singletons.TryGetValue(viewId, out var exact) && exact != null)
            {
                view = exact.View;
                return true;
            }
            for (int i = _openOrder.Count - 1; i >= 0; i--)
            {
                UIDisplay display = _openOrder[i];
                if (display != null && (display.ViewId == viewId || display.name == viewId))
                {
                    view = display.View;
                    return true;
                }
            }
            view = null;
            return false;
        }

        public void BringToFront(UIView view) => BringToFront(view?.Display);

        public void BringToFront(UIDisplay display)
        {
            if (display == null || !_openOrder.Remove(display)) return;
            _openOrder.Add(display);
            display.transform.SetAsLastSibling();
            if (!display.gameObject.activeSelf) display.gameObject.SetActive(true);
            FocusTop();
            RefreshBackground();
        }

        public void SetLayer(UIView view, UILayer layer) => SetLayer(view?.Display, layer);

        public void SetLayer(UIDisplay display, UILayer layer)
        {
            if (display == null || !_openOrder.Contains(display)) return;
            display.CurrentLayer = layer;
            display.transform.SetParent(GetLayerRoot(layer), false);
            ApplyLayout(display);
            BringToFront(display);
        }

        public void Hide(UIView view) => Hide(view?.Display);

        public void Hide(UIDisplay display)
        {
            if (display == null || !_openOrder.Contains(display) || !display.gameObject.activeSelf) return;
            display.BlurInternal();
            display.gameObject.SetActive(false);
            FocusTop();
            RefreshBackground();
        }

        public void Show(UIView view) => Show(view?.Display);

        public void Show(UIDisplay display)
        {
            if (display == null || !_openOrder.Contains(display)) return;
            if (!display.gameObject.activeSelf) display.gameObject.SetActive(true);
            BringToFront(display);
            display.GetComponent<UITransition>()?.PlayOpen();
        }

        internal void NotifyViewDestroyed(UIDisplay display)
        {
            // Unity 对象执行 OnDestroy 时会与 null 比较相等；这里必须使用
            // ReferenceEquals，才能继续移除这条已经失效的记录。
            if (_shuttingDown || ReferenceEquals(display, null)) return;
            _openOrder.Remove(display);
            RemoveSingleton(display);
            ReleaseDisplayLease(display);
            FocusTop();
            RefreshBackground();
        }

        internal static void ReleaseHost(UIRuntimeHost host)
        {
            UIManager manager = _instance;
            if (manager == null || !ReferenceEquals(manager._host, host)) return;
            manager.ShutdownInternal();
            manager._host = null;
            _instance = null;
        }

        internal void RefreshCanvasSettings(UIRuntimeHost host)
        {
            if (_host != host || _shuttingDown) return;
            EnsureRootCanvas();
            int unityLayer = _host.UILayer;
            foreach (RectTransform root in _layerRoots.Values)
                if (root != null) SetUnityLayerRecursively(root, unityLayer);
        }

        private UIDisplay OpenPrefab(GameObject prefab, object args, UILayer? layer, out bool created)
        {
            PruneDestroyedDisplays();
            UIDisplay prefabDisplay = prefab.GetComponent<UIDisplay>();
            if (prefabDisplay == null)
                throw new InvalidOperationException($"UI prefab '{prefab.name}' must contain UIDisplay on its root.");
            if (!prefabDisplay.ManagedAsView)
                throw new InvalidOperationException($"UI prefab '{prefab.name}' is marked as an Item, not a managed View.");

            UILayer targetLayer = layer ?? prefabDisplay.DefaultLayer;
            string key = ResolveKey(prefabDisplay, prefab.name);
            if (prefabDisplay.SingleInstance && TryGetOpen(key, out var existing))
            {
                created = false;
                return ActivateExisting(existing, args, targetLayer);
            }

            GameObject instanceObject = Object.Instantiate(prefab, GetLayerRoot(targetLayer), false);
            instanceObject.name = prefab.name;
            var instance = instanceObject.GetComponent<UIDisplay>();
            try
            {
                RegisterOpenedDisplay(instance, key, targetLayer, args);
                created = true;
                return instance;
            }
            catch
            {
                _openOrder.Remove(instance);
                RemoveSingleton(instance);
                DestroyUnityObject(instanceObject);
                created = false;
                throw;
            }
        }

        private void RegisterOpenedDisplay(UIDisplay display, string key, UILayer layer, object args)
        {
            display.transform.SetParent(GetLayerRoot(layer), false);
            RemoveRootCanvasComponents(display);
            SetUnityLayerRecursively(display.transform, _host.UILayer);
            display.Initialize(this, layer);
            ApplyLayout(display);
            if (!_openOrder.Contains(display)) _openOrder.Add(display);
            if (display.SingleInstance) _singletons[key] = display;
            display.OpenInternal(args);
            display.transform.SetAsLastSibling();
            display.GetComponent<UITransition>()?.PlayOpen();
            FocusTop();
            RefreshBackground();
        }

        private UIDisplay ActivateExisting(UIDisplay display, object args, UILayer layer)
        {
            if (display.CurrentLayer != layer) SetLayer(display, layer);
            if (!display.gameObject.activeSelf) display.gameObject.SetActive(true);
            display.OpenInternal(args);
            BringToFront(display);
            return display;
        }

        private bool TryGetOpen(string key, out UIDisplay display)
        {
            if (_singletons.TryGetValue(key, out display) && display != null) return true;
            if (display == null) _singletons.Remove(key);
            return false;
        }

        private void RemoveSingleton(UIDisplay display)
        {
            string removeKey = null;
            foreach (var pair in _singletons)
                if (ReferenceEquals(pair.Value, display)) { removeKey = pair.Key; break; }
            if (removeKey != null) _singletons.Remove(removeKey);
        }

        private static string ResolveKey(UIDisplay display, string fallbackName)
        {
            if (display != null && !string.IsNullOrWhiteSpace(display.ViewId)) return display.ViewId;
            string logicType = display?.ViewTypeName;
            return $"{logicType ?? typeof(UIView).FullName}:{fallbackName}";
        }

        private UIDisplay FindTopVisible(bool requireCloseOnBack = false, bool requireBackground = false)
        {
            UIDisplay result = null;
            int highestLayer = int.MinValue;
            int highestIndex = -1;
            for (int i = 0; i < _openOrder.Count; i++)
            {
                UIDisplay display = _openOrder[i];
                if (display == null || !display.IsOpen || !display.gameObject.activeSelf) continue;
                if (requireCloseOnBack && !display.CloseOnBack) continue;
                if (requireBackground && !display.UsesBackground) continue;
                int visualLayer = (int)display.CurrentLayer;
                if (visualLayer < highestLayer || (visualLayer == highestLayer && i < highestIndex)) continue;
                result = display;
                highestLayer = visualLayer;
                highestIndex = i;
            }
            return result;
        }

        private void FocusTop()
        {
            UIDisplay top = FindTopVisible();
            for (int i = 0; i < _openOrder.Count; i++)
                if (_openOrder[i] != null && _openOrder[i] != top) _openOrder[i].BlurInternal();
            top?.FocusInternal();
        }

        private void RefreshBackground()
        {
            UIDisplay owner = FindTopVisible(requireBackground: true);
            if (owner == null)
            {
                if (_background != null) _background.gameObject.SetActive(false);
                return;
            }
            EnsureBackground();
            Transform parent = GetLayerRoot(owner.CurrentLayer);
            _background.transform.SetParent(parent, false);
            Stretch((RectTransform)_background.transform);
            _background.gameObject.SetActive(true);
            _background.Configure(
                owner.BackgroundColor,
                owner.BackgroundMode == UIBackgroundMode.Modal,
                owner.CloseOnBackground ? (Action)(() => Close(owner)) : null);
            _background.transform.SetSiblingIndex(Mathf.Max(0, owner.transform.GetSiblingIndex()));
            owner.transform.SetAsLastSibling();
        }

        private void EnsureBackground()
        {
            if (_background != null) return;
            GameObject background;
            if (_host != null && _host.BackgroundPrefab != null)
                background = Object.Instantiate(_host.BackgroundPrefab);
            else
                background = new GameObject(
                    "[Background]", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            if (!(background.transform is RectTransform))
                throw new InvalidOperationException("UI shared Background prefab root must use RectTransform.");
            SetUnityLayerRecursively(background.transform, _host.UILayer);
            _background = background.GetComponent<UIBackground>() ?? background.AddComponent<UIBackground>();
            background.SetActive(false);
        }

        private void ReleaseDisplayLease(UIDisplay display)
        {
            if (!_assetLeases.TryGetValue(display, out var lease)) return;
            _assetLeases.Remove(display);
            if (_host != null && Application.isPlaying) _host.DisposeAfterFrame(lease);
            else lease.Dispose();
        }

        private void PruneDestroyedDisplays()
        {
            for (int i = _openOrder.Count - 1; i >= 0; i--)
            {
                UIDisplay display = _openOrder[i];
                if (display != null) continue;
                _openOrder.RemoveAt(i);
                ReleaseDisplayLease(display);
            }

            List<string> deadSingletons = null;
            foreach (var pair in _singletons)
            {
                if (pair.Value != null) continue;
                if (deadSingletons == null) deadSingletons = new List<string>();
                deadSingletons.Add(pair.Key);
            }
            if (deadSingletons == null) return;
            for (int i = 0; i < deadSingletons.Count; i++)
                _singletons.Remove(deadSingletons[i]);
        }

        private static void ApplyLayout(UIDisplay display)
        {
            if (display.StretchToLayer && display.transform is RectTransform rect) Stretch(rect);
        }

        private static void SetUnityLayerRecursively(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetUnityLayerRecursively(root.GetChild(i), layer);
        }

        private static void RemoveRootCanvasComponents(UIDisplay display)
        {
            // 受管理 View 复用框架创建的分层 Canvas。Prefab 根节点残留的独立
            // Canvas（尤其是 World Space）会脱离该渲染链；子节点 Canvas 不处理。
            GraphicRaycaster raycaster = display.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;
                DestroyUnityObject(raycaster);
            }

            CanvasScaler scaler = display.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.enabled = false;
                DestroyUnityObject(scaler);
            }

            Canvas canvas = display.GetComponent<Canvas>();
            if (canvas == null) return;
            canvas.enabled = false;
            DestroyUnityObject(canvas);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void EnsureAvailable()
        {
            if (_shuttingDown || _quitting) throw new InvalidOperationException("UIManager is shutting down.");
            EnsureHost();
            if (_host == null) throw new InvalidOperationException("UIRuntimeHost could not be created.");
        }

        private void EnsureHost()
        {
            if (_host != null || _shuttingDown || _quitting) return;
            var existing = Object.FindObjectOfType<UIRuntimeHost>(true);
            if (existing != null) { AttachHost(existing); return; }
            // 先创建完整 Canvas 组件栈，再由 UIRuntimeHost.Awake 关联 Manager，
            // 避免编辑模式或启动阶段首次创建层级根节点时发生 AddComponent/OnValidate 重入。
            var hostObject = new GameObject(
                "[UI]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var host = hostObject.AddComponent<UIRuntimeHost>();
            if (_host == null) AttachHost(host);
        }

        private void AttachHost(UIRuntimeHost host)
        {
            if (host == null || (_host != null && _host != host)) return;
            _host = host;
            host.SetManager(this);
            _shuttingDown = false;
            BuildLayerRoots();
        }

        private void ShutdownInternal()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            for (int i = _openOrder.Count - 1; i >= 0; i--)
                if (_openOrder[i] != null) _openOrder[i].CloseInternal();
            foreach (var lease in _assetLeases.Values) lease?.Dispose();
            _assetLeases.Clear();
            ReleaseAllPreloaded();
            _openOrder.Clear();
            _singletons.Clear();
            _layerRoots.Clear();
            _background = null;
        }

        private void BuildLayerRoots()
        {
            if (_host == null) return;
            EnsureRootCanvas();
            _layerRoots.Clear();
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer))) CreateLayerRoot(layer);
        }

        private void EnsureRootCanvas()
        {
            if (_host == null) return;
            GameObject root = _host.gameObject;
            var canvas = root.GetComponent<Canvas>() ?? root.AddComponent<Canvas>();
            canvas.renderMode = _host.RenderMode == UIRenderMode.ScreenSpaceCamera
                ? RenderMode.ScreenSpaceCamera
                : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = _host.ResolveUICamera();
            canvas.planeDistance = _host.PlaneDistance;
            root.layer = _host.UILayer;
            var scaler = root.GetComponent<CanvasScaler>() ?? root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _host.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = _host.MatchWidthOrHeight;
            if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
        }

        private RectTransform CreateLayerRoot(UILayer layer)
        {
            if (_host == null) return null;
            if (_layerRoots.TryGetValue(layer, out var existing) && existing != null) return existing;
            string layerName = $"[{layer}]";
            Transform found = _host.transform.Find(layerName);
            GameObject layerObject = found == null
                ? new GameObject(layerName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster))
                : found.gameObject;
            if (found == null) layerObject.transform.SetParent(_host.transform, false);
            layerObject.layer = _host.UILayer;
            var rect = (RectTransform)layerObject.transform;
            Stretch(rect);
            var canvas = layerObject.GetComponent<Canvas>() ?? layerObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)layer;
            if (layerObject.GetComponent<GraphicRaycaster>() == null) layerObject.AddComponent<GraphicRaycaster>();
            _layerRoots[layer] = rect;
            return rect;
        }

        private static AssetModule RequireAssets()
        {
            var assets = AssetModule.Instance;
            if (assets == null || !assets.IsReady)
                throw new InvalidOperationException(
                    "AssetModule is not ready. Register its loader and await InitializeAsync before loading UI.");
            return assets;
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("UI prefab location is required.", nameof(location));
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
