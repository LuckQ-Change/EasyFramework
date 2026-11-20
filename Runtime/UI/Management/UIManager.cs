using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.UI
{
    public readonly struct UIPageResourceUsage
    {
        public Type ViewType { get; }
        public bool Visible { get; }
        public int Leases { get; }
        public long EstimatedBytes { get; }

        internal UIPageResourceUsage(Type viewType, bool visible, int leases, long estimatedBytes)
        {
            ViewType = viewType;
            Visible = visible;
            Leases = leases;
            EstimatedBytes = estimatedBytes;
        }
    }

    public sealed class UIManager
    {
        private sealed class Window
        {
            public UIDisplay Display;
            public UIView View;
            public AssetLease<GameObject> Lease;
            public bool Retiring;
            public bool Finished;
            public readonly TaskCompletionSource<bool> Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public UIPageKind Kind;
        }

        private static bool _quitting;
        private readonly Dictionary<Type, Window> _windows = new();
        private readonly List<Window> _order = new();
        private readonly Dictionary<UILayer, RectTransform> _layers = new();
        private UIRuntimeHost _host;
        private UIBackground _background;
        private Transform _inactiveRoot;
        private CancellationTokenSource _screenRequest;
        private bool _stopping;

        public static UIManager Current { get; private set; }
        public static UIManager Instance
        {
            get
            {
                if (_quitting) return null;
                if (Current != null) return Current;
                UIRuntimeHost host = Object.FindObjectOfType<UIRuntimeHost>(true);
                if (host == null)
                {
                    var root = new GameObject("[UI]", typeof(RectTransform), typeof(Canvas),
                        typeof(CanvasScaler), typeof(GraphicRaycaster));
                    host = root.AddComponent<UIRuntimeHost>();
                }
                TryAttachHost(host, out UIManager manager);
                return manager;
            }
        }

        public UIRuntimeHost RuntimeHost => _host;
        public int OpenCount => _order.Count;

        public IReadOnlyList<UIPageResourceUsage> GetResourceSnapshot()
        {
            var pages = new List<UIPageResourceUsage>(_order.Count);
            foreach (Window window in _order)
            {
                UIDisplay display = window.Display;
                if (display == null) continue;
                bool prefabHeld = window.Lease.IsValid;
                int leases = prefabHeld ? 1 : 0;
                long bytes = prefabHeld ? window.Lease.Entry.Bytes : 0;
                foreach (UIBindingRoot binding in display.GetComponentsInChildren<UIBindingRoot>(true))
                    binding.RuntimeView?.AddResourceUsage(ref leases, ref bytes);
                pages.Add(new UIPageResourceUsage(window.View.GetType(), display.gameObject.activeInHierarchy, leases, bytes));
            }
            return pages;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Current = null;
            _quitting = false;
        }

        internal static bool TryAttachHost(UIRuntimeHost host, out UIManager manager)
        {
            manager = null;
            if (_quitting || (Current != null && Current._host != host)) return false;
            if (Current == null)
            {
                Current = new UIManager { _host = host };
                host.SetManager(Current);
                Current.RefreshCanvasSettings(host);
            }
            manager = Current;
            return true;
        }

        internal static void NotifyApplicationQuit()
        {
            _quitting = true;
            Current?.Stop();
        }

        internal static void ReleaseHost(UIRuntimeHost host)
        {
            if (Current == null || !ReferenceEquals(Current._host, host)) return;
            Current.Stop();
            Current = null;
        }

        public T Get<T>() where T : UIView =>
            _windows.TryGetValue(typeof(T), out Window window) && !window.Retiring ? (T)window.View : null;

        internal Task<T> OpenAsync<T>(object args, UIPageKind kind, CancellationToken token, Action<T> prepare = null)
            where T : UIView, new() => kind == UIPageKind.Screen
                ? OpenScreenAsync<T>(args, token)
                : OpenCoreAsync(args, kind, token, prepare);

        private async Task<T> OpenScreenAsync<T>(object args, CancellationToken token) where T : UIView, new()
        {
            EnsureAvailable(token);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationTokenSource previous = _screenRequest;
            _screenRequest = request;
            previous?.Cancel();
            try { return await OpenCoreAsync<T>(args, UIPageKind.Screen, request.Token, null); }
            finally
            {
                if (ReferenceEquals(_screenRequest, request)) _screenRequest = null;
            }
        }

        private async Task<T> OpenCoreAsync<T>(object args, UIPageKind kind, CancellationToken token, Action<T> prepare)
            where T : UIView, new()
        {
            EnsureAvailable(token);
            Type type = typeof(T);
            if (_windows.TryGetValue(type, out Window existing) && !existing.Display.IsClosing)
                return PresentExisting(existing, args, prepare);

            string address = AddressOf(type);
            AssetLease<GameObject> lease = await RequireAssets().AcquireAsync<GameObject>(address, token);
            try
            {
                while (_windows.TryGetValue(type, out existing))
                {
                    EnsureAvailable(token);
                    if (!existing.Display.IsClosing) return PresentExisting(existing, args, prepare);
                    await WaitForCloseAsync(existing, token);
                }
                EnsureAvailable(token);
                UIDisplay template = lease.Asset.GetComponent<UIDisplay>();
                if (template == null || template.Kind != kind ||
                    !string.Equals(template.PrefabLocation, address, StringComparison.Ordinal) ||
                    UIFactory.ResolveType(template.ViewTypeName) != type)
                    throw new InvalidOperationException($"{type.Name} 的 Prefab 类型、用途或资源地址不匹配。");

                UILayer layer = kind == UIPageKind.Popup ? UILayer.Popup : UILayer.Screen;
                var view = new T();
                UIDisplay display = InstantiateHidden(template, GetLayerRoot(layer));
                display.name = template.name;
                SetLayer(display.transform, _host.UILayer);
                var window = new Window { Display = display, View = view, Lease = lease, Kind = kind };
                lease = null;
                _windows.Add(type, window);
                _order.Add(window);
                display.Attach(this, view);
                try
                {
                    prepare?.Invoke(view);
                    view.SetArguments(args);
                    view.Initialize(display);
                    EnsureAvailable(token);
                    if (!display.IsClosing)
                    {
                        Present(window);
                        if (!display.IsClosing && display.gameObject.activeSelf)
                            display.GetComponent<UITransition>()?.PlayOpen();
                    }
                }
                catch
                {
                    Retire(window);
                    throw;
                }
                return view;
            }
            finally { lease?.Dispose(); }
        }

        private UIDisplay InstantiateHidden(UIDisplay template, RectTransform layer)
        {
            if (!template.gameObject.activeSelf) return Object.Instantiate(template, layer, false);
            if (_inactiveRoot == null)
            {
                var staging = new GameObject("[UI Staging]", typeof(RectTransform));
                staging.SetActive(false);
                _inactiveRoot = staging.transform;
                _inactiveRoot.SetParent(_host.transform, false);
            }
            UIDisplay display = Object.Instantiate(template, _inactiveRoot, false);
            display.gameObject.SetActive(false);
            display.transform.SetParent(layer, false);
            return display;
        }

        private T PresentExisting<T>(Window window, object args, Action<T> prepare) where T : UIView
        {
            var view = (T)window.View;
            prepare?.Invoke(view);
            try
            {
                view.SetArguments(args);
                _order.Remove(window);
                _order.Add(window);
                Present(window);
                return view;
            }
            catch
            {
                Retire(window);
                throw;
            }
        }

        private void Present(Window window)
        {
            window.Display.transform.SetAsLastSibling();
            if (window.Kind == UIPageKind.Screen)
                SyncScreens(window);
            else
                Reveal(window);
            SyncBackdrop();
        }

        public Task BackAsync()
        {
            Window window = Top(UIPageKind.Popup);
            if (window == null)
            {
                if (CancelScreenOpen()) return Task.CompletedTask;
                window = Top(UIPageKind.Screen);
            }
            return window != null && window.Display.CloseOnBack ? Close(window) : Task.CompletedTask;
        }

        private bool CancelScreenOpen()
        {
            CancellationTokenSource request = _screenRequest;
            _screenRequest = null;
            request?.Cancel();
            return request != null;
        }

        private static async Task WaitForCloseAsync(Window window, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => canceled.TrySetResult(true));
            await Task.WhenAny(window.Closed.Task, canceled.Task);
            token.ThrowIfCancellationRequested();
        }

        internal Task CloseAsync(UIDisplay display)
        {
            Window window = Find(display);
            return window == null ? Task.CompletedTask : Close(window);
        }

        private Task Close(Window window)
        {
            UIDisplay display = window.Display;
            if (display.IsClosing) return window.Closed.Task;
            display.IsClosing = true;
            Suspend(window);
            if (display == null) Retire(window);
            else
            {
                var transition = display.GetComponent<UITransition>();
                if (Application.isPlaying && display.gameObject.activeInHierarchy && transition != null)
                {
                    try { transition.PlayClose(() => Retire(window)); }
                    catch (Exception exception)
                    {
                        Log.Error("[UI] 退场失败。", exception);
                        Retire(window);
                    }
                }
                else Retire(window);
            }
            return window.Closed.Task;
        }

        internal void NotifyDestroyed(UIDisplay display)
        {
            Window window = Find(display);
            if (window != null) Retire(window, destroy: false);
        }

        private void Retire(Window window, bool destroy = true)
        {
            if (window.Retiring) return;
            window.Retiring = true;
            window.Display.IsClosing = true;
            _order.Remove(window);
            if (destroy && window.Display != null)
            {
                window.Display.gameObject.SetActive(false);
                window.Display.DisposeContent();
                Destroy(window.Display.gameObject);
            }

            if (!_stopping && Application.isPlaying && _host != null)
                _host.DisposeAfterFrame(new ActionDisposable(() => Finish(window)));
            else
                Finish(window);

            if (_stopping) return;
            try { SyncScreens(); SyncBackdrop(); }
            catch (Exception exception) { Log.Error("[UI] 恢复上一界面失败。", exception); }
        }

        private void Finish(Window window)
        {
            if (window.Finished) return;
            window.Finished = true;
            _windows.Remove(window.View.GetType());
            try { window.Lease.Dispose(); }
            catch (Exception exception) { Log.Error("[UI] Prefab 释放失败。", exception); }
            finally
            {
                window.View.CompleteResult();
                window.Closed.TrySetResult(true);
            }
        }

        private void SyncScreens(Window refresh = null)
        {
            Window top = Top(UIPageKind.Screen);
            foreach (Window page in _order.ToArray())
            {
                if (page.Kind != UIPageKind.Screen || page == top || page.Display == null ||
                    page.Display.IsClosing || !page.Display.gameObject.activeSelf) continue;
                Suspend(page);
                if (page.Display != null && !page.Display.IsClosing) page.Display.gameObject.SetActive(false);
            }
            if (top != null && top.Display != null && !top.Display.IsClosing &&
                (top == refresh || !top.Display.gameObject.activeSelf) && Top(UIPageKind.Screen) == top)
                Reveal(top);
        }

        private static void Reveal(Window window)
        {
            window.Display.gameObject.SetActive(true);
            if (window.Display != null && !window.Display.IsClosing) window.View.RefreshView();
        }

        private static void Suspend(Window window)
        {
            try { window.View.Suspend(); }
            catch (Exception exception) { Log.Error("[UI] 隐藏界面失败。", exception); }
        }

        private Window Top(UIPageKind kind)
        {
            for (int i = _order.Count - 1; i >= 0; i--)
                if (_order[i].Display != null && _order[i].Kind == kind) return _order[i];
            return null;
        }

        private Window Find(UIDisplay display)
        {
            foreach (Window window in _windows.Values)
                if (ReferenceEquals(window.Display, display)) return window;
            return null;
        }

        private void SyncBackdrop()
        {
            Window popup = Top(UIPageKind.Popup);
            if (popup == null)
            {
                if (_background != null) _background.gameObject.SetActive(false);
                return;
            }
            if (_background == null)
            {
                RectTransform layer = GetLayerRoot(UILayer.Popup);
                GameObject root = _host.BackgroundPrefab != null
                    ? Object.Instantiate(_host.BackgroundPrefab, layer, false)
                    : new GameObject("[Popup Background]", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                if (!(root.transform is RectTransform rect))
                {
                    Destroy(root);
                    throw new InvalidOperationException("弹窗遮罩 Prefab 的根节点需要 RectTransform。");
                }
                rect.SetParent(layer, false);
                Stretch(rect);
                SetLayer(rect, _host.UILayer);
                _background = root.GetComponent<UIBackground>() ?? root.AddComponent<UIBackground>();
            }
            _background.gameObject.SetActive(true);
            _background.transform.SetAsLastSibling();
            popup.Display.transform.SetAsLastSibling();
            _background.Configure(popup.Display.BackgroundColor, true,
                popup.Display.CloseOnBackground && !popup.Display.IsClosing ? () => _ = Close(popup) : null);
        }

        private void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            CancelScreenOpen();
            foreach (Window window in new List<Window>(_windows.Values))
            {
                Retire(window);
                Finish(window);
            }
        }

        private void EnsureAvailable(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_stopping || _quitting || _host == null) throw new ObjectDisposedException(nameof(UIManager));
        }

        internal static AssetModule RequireAssets() => AssetModule.Instance ??
            throw new InvalidOperationException("请在 Startup Config 配置 UI 资源包。");

        internal static string AddressOf(Type type)
        {
            string address = UIFactory.Address(type);
            if (!string.IsNullOrWhiteSpace(address)) return address;
            throw new InvalidOperationException($"{type.Name} 缺少 UI 资源地址。");
        }

        public RectTransform GetLayerRoot(UILayer layer)
        {
            if (_layers.TryGetValue(layer, out RectTransform rect) && rect != null) return rect;
            var root = new GameObject($"[{layer}]", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            rect = (RectTransform)root.transform;
            rect.SetParent(_host.transform, false);
            Stretch(rect);
            root.layer = _host.UILayer;
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (int)layer;
            _layers[layer] = rect;
            return rect;
        }

        internal void RefreshCanvasSettings(UIRuntimeHost host)
        {
            if (_host != host) return;
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = host.RenderMode == UIRenderMode.ScreenSpaceCamera
                ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = host.ResolveUICamera();
            canvas.planeDistance = host.PlaneDistance;
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = host.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = host.MatchWidthOrHeight;
            SetLayer(host.transform, host.UILayer);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetLayer(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            foreach (Transform child in node) SetLayer(child, layer);
        }

        private static void Destroy(GameObject target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
