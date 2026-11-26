using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.UI
{
    /// <summary>记录一个页面在查询时持有的资源。</summary>
    public readonly struct UIPageResourceUsage
    {
        /// <summary>页面的 View 类型。</summary>
        public Type ViewType { get; }
        /// <summary>页面在宿主中是否可见。</summary>
        public bool Visible { get; }
        /// <summary>页面持有的资源租约数量。</summary>
        public int Leases { get; }
        /// <summary>页面所持资源的估算字节数。</summary>
        public long EstimatedBytes { get; }

        internal UIPageResourceUsage(Type viewType, bool visible, int leases, long estimatedBytes)
        {
            ViewType = viewType;
            Visible = visible;
            Leases = leases;
            EstimatedBytes = estimatedBytes;
        }
    }

    /// <summary>管理默认 UI 宿主的页面实例、导航和资源租约。</summary>
    public sealed class UIManager
    {
        /// <summary>一次开页使用的页面声明及实例创建方式。</summary>
        private readonly struct PageRoute
        {
            private readonly UIPageFactoryBinding _factory;
            internal readonly UIPageEntry Entry;

            internal Type ViewType => _factory.ViewType;
            internal UIPageKind Kind => Entry.Kind;
            internal string Address => Entry.PrefabAddress;

            internal PageRoute(UIPageEntry entry, UIPageFactoryBinding factory)
            {
                Entry = entry;
                _factory = factory;
            }

            internal UIView Create() => _factory.Create();

            internal bool Matches(UIDisplay display) => display != null && display.Kind == Kind &&
                string.Equals(display.ViewScriptGuid, Entry.FactoryKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>持有页面、导航规则和关闭所需的状态。</summary>
        private sealed class Window
        {
            public UIDisplay Display;
            public UIView View;
            public AssetLease<GameObject> Lease;
            public bool Retiring;
            public bool Finished;
            public readonly TaskCompletionSource<bool> Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public UIPageEntry Entry;
            public UIPageKind Kind => Entry.Kind;
        }

        private static bool _quitting;
        private static UIManager _current;
        private readonly Dictionary<Type, Window> _windows = new();
        private readonly HashSet<Type> _constructingViews = new();
        private readonly List<Window> _order = new();
        private readonly Dictionary<UILayer, RectTransform> _layers = new();
        private UIRuntimeHost _host;
        private UIPageManifest _pageManifest;
        private UIBackground _background;
        private Transform _inactiveRoot;
        private CancellationTokenSource _screenRequest;
        private bool _stopping;
        private int _openingPages;

        internal static bool HasDefaultPresentationScope
        {
            get
            {
                ModuleManager scope = ModuleManager.Current;
                return scope == null || ReferenceEquals(scope, ModuleManager.Default);
            }
        }

        /// <summary>读取默认宿主中已有的 UI 管理器。</summary>
        public static UIManager Current => HasDefaultPresentationScope ? _current : null;

        /// <summary>获取或创建默认宿主的 UI 管理器。</summary>
        public static UIManager Instance
        {
            get
            {
                if (_quitting || !HasDefaultPresentationScope) return null;
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

        /// <summary>当前管理器使用的 Unity UI 宿主。</summary>
        public UIRuntimeHost RuntimeHost => _host;
        /// <summary>导航栈中的页面数量。</summary>
        public int OpenCount => _order.Count;

        /// <summary>获取各页面当前占用的资源租约。</summary>
        /// <returns>当前导航栈中的页面资源快照。</returns>
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

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _current = null;
            _quitting = false;
        }

        internal static bool TryAttachHost(UIRuntimeHost host, out UIManager manager)
        {
            manager = null;
            if (_quitting || !HasDefaultPresentationScope || (_current != null && _current._host != host)) return false;
            if (_current == null)
            {
                _current = new UIManager
                {
                    _host = host,
                    _pageManifest = FrameworkLauncher.Instance?.ActiveConfig?.PageManifest,
                };
                host.SetManager(_current);
                _current.RefreshCanvasSettings(host);
            }
            manager = _current;
            return true;
        }

        internal static void NotifyApplicationQuit()
        {
            _quitting = true;
            _current?.Stop();
        }

        internal static void ReleaseHost(UIRuntimeHost host)
        {
            if (_current == null || !ReferenceEquals(_current._host, host)) return;
            _current.Stop();
            _current = null;
        }

        internal void SetPageManifest(UIPageManifest manifest)
        {
            if (ReferenceEquals(_pageManifest, manifest)) return;
            // 页面实例和在途开页归属于同一份清单，避免配置重用时混入两套地址和工厂。
            if (_windows.Count != 0 || _openingPages != 0)
                throw new InvalidOperationException("请关闭现有页面并结束在途开页，再更换 UI 页面清单。");
            _pageManifest = manifest;
        }

        #endregion

        #region 公共方法

        /// <summary>获取当前由宿主管理的指定类型页面。</summary>
        /// <typeparam name="T">页面的 View 类型。</typeparam>
        /// <returns>已存在且尚未退出导航栈的页面，未打开时为空。</returns>
        public T Get<T>() where T : UIView =>
            _windows.TryGetValue(typeof(T), out Window window) && !window.Retiring ? (T)window.View : null;

        /// <summary>返回时优先处理顶层弹窗，其次处理切换中的页面和顶层主界面。</summary>
        /// <returns>当前导航操作完成的任务。</returns>
        public Task BackAsync()
        {
            Window window = Top(UIPageKind.Popup);
            if (window == null)
            {
                if (CancelScreenOpen()) return Task.CompletedTask;
                window = Top(UIPageKind.Screen);
            }
            bool canClose = window != null && window.Entry.CanCloseOnBack;
            return canClose ? Close(window) : Task.CompletedTask;
        }

        #endregion

        #region 私有逻辑

        internal async Task<T> OpenAsync<T>(object args, UIPageKind kind, CancellationToken token, Action<T> prepare = null)
            where T : UIView
        {
            EnsureAvailable(token);
            Type type = typeof(T);
            PageRoute route = ResolveRoute(RequireEntry(_pageManifest, type));
            if (route.ViewType != type || route.Kind != kind)
                throw new InvalidOperationException($"{type.Name} 的页面工厂或导航类型与开页方式不匹配。");

            Action<UIView> beforePresent = prepare == null ? null : view => prepare((T)view);
            return (T)await OpenPageAsync(route, args, token, beforePresent);
        }

        internal async Task<UIView> OpenAsync(string pageId, object args, CancellationToken token)
        {
            EnsureAvailable(token);
            PageRoute route = ResolveRoute(RequireEntry(_pageManifest, pageId));
            Action<UIView> prepare = route.Kind == UIPageKind.Popup ? static view => view.CompleteResult() : null;
            return await OpenPageAsync(route, args, token, prepare);
        }

        private async Task<UIView> OpenPageAsync(PageRoute route, object args, CancellationToken token, Action<UIView> prepare)
        {
            if (_constructingViews.Contains(route.ViewType))
                throw new InvalidOperationException($"页面「{route.Entry.Id}」正在创建，工厂不能再次打开该页面。");
            _openingPages++;
            try
            {
                if (route.Kind != UIPageKind.Screen) return await OpenCoreAsync(route, args, token, prepare);
                return await OpenScreenAsync(route, args, token);
            }
            finally { _openingPages--; }
        }

        private async Task<UIView> OpenScreenAsync(PageRoute route, object args, CancellationToken token)
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationTokenSource previous = _screenRequest;
            _screenRequest = request;
            previous?.Cancel();
            try { return await OpenCoreAsync(route, args, request.Token, null); }
            finally
            {
                if (ReferenceEquals(_screenRequest, request)) _screenRequest = null;
            }
        }

        private async Task<UIView> OpenCoreAsync(PageRoute route, object args, CancellationToken token, Action<UIView> prepare)
        {
            EnsureAvailable(token);
            Type type = route.ViewType;
            if (_windows.TryGetValue(type, out Window existing) && !existing.Display.IsClosing)
                return PresentExisting(existing, args, prepare);

            AssetLease<GameObject> lease = await RequireAssets().AcquireAsync<GameObject>(route.Address, token);
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
                if (!route.Matches(template))
                    throw new InvalidOperationException($"页面「{route.Entry.Id}」的 Prefab 与开页声明不匹配（{route.Address}）。");

                Window window = CreateWindow(route, template, lease, token);
                UIView view = window.View;
                UIDisplay display = window.Display;
                _windows.Add(type, window);
                lease = null;
                _order.Add(window);
                display.Attach(this, view);
                try
                {
                    view.SetArguments(args);
                    prepare?.Invoke(view);
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

        private Window CreateWindow(PageRoute route, UIDisplay template, AssetLease<GameObject> lease, CancellationToken token)
        {
            _constructingViews.Add(route.ViewType);
            UIView view = null;
            UIDisplay display = null;
            try
            {
                UIView created = route.Create();
                if (created.IsDisposed || created.HasDisplay)
                    throw new InvalidOperationException($"页面「{route.Entry.Id}」的工厂须创建新的 {route.ViewType.Name} 实例。");
                view = created;
                EnsureAvailable(token);
                UILayer layer = route.Kind == UIPageKind.Popup ? UILayer.Popup : UILayer.Screen;
                display = InstantiateHidden(template, GetLayerRoot(layer));
                display.name = template.name;
                SetLayer(display.transform, _host.UILayer);
                return new Window { Display = display, View = view, Lease = lease, Entry = route.Entry };
            }
            catch
            {
                if (display != null) Destroy(display.gameObject);
                try { view?.Dispose(); }
                catch (Exception exception) { Log.Error("[UI] 页面工厂回收失败。", exception); }
                throw;
            }
            finally { _constructingViews.Remove(route.ViewType); }
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

        private UIView PresentExisting(Window window, object args, Action<UIView> prepare)
        {
            UIView view = window.View;
            // 参数类型错误时保留当前页面和它正在等待的弹窗结果。
            view.SetArguments(args);
            prepare?.Invoke(view);
            try
            {
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

        private bool CancelScreenOpen()
        {
            CancellationTokenSource request = _screenRequest;
            _screenRequest = null;
            request?.Cancel();
            return request != null;
        }

        private static async Task WaitForCloseAsync(Window window, CancellationToken token)
        {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
            // 关闭动画的回调会完成原始任务，随后在 Unity 主线程检查取消。
            while (!window.Closed.Task.IsCompleted && !token.IsCancellationRequested)
                await Task.Yield();
#else
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => canceled.TrySetResult(true));
            await Task.WhenAny(window.Closed.Task, canceled.Task);
#endif
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
            bool canClose = popup.Entry.CanCloseOnBackground;
            _background.Configure(popup.Display.BackgroundColor, true,
                canClose && !popup.Display.IsClosing ? () => _ = Close(popup) : null);
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

        internal static string AddressOf(Type type) => RequireEntry(ActivePageManifest(), type).PrefabAddress;

        internal static string AddressOf(string pageId) => RequireEntry(ActivePageManifest(), pageId).PrefabAddress;

        private static UIPageManifest ActivePageManifest()
        {
            UIManager manager = Current;
            if (manager != null) return manager._pageManifest;
            return FrameworkLauncher.Instance?.ActiveConfig?.PageManifest;
        }

        private static UIPageEntry RequireEntry(UIPageManifest manifest, Type type)
        {
            if (manifest == null) throw new InvalidOperationException("请先在 Startup Config 配置 UI 页面清单。");
            string key = UIPageFactories.FindKey(type);
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException($"{type.Name} 尚未注册页面工厂。请从清单同步生成脚本。");
            return manifest.FindByFactoryKey(key) ??
                   throw new InvalidOperationException($"{type.Name} 尚未登记在当前 UI 页面清单。");
        }

        private static UIPageEntry RequireEntry(UIPageManifest manifest, string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) throw new ArgumentException("页面 ID 不能为空。", nameof(pageId));
            if (manifest == null) throw new InvalidOperationException("请先在 Startup Config 配置 UI 页面清单。");
            return manifest.FindById(pageId) ??
                   throw new InvalidOperationException($"页面「{pageId}」尚未登记在当前 UI 页面清单。");
        }

        private static PageRoute ResolveRoute(UIPageEntry entry)
        {
            UIPageFactoryBinding factory = UIPageFactories.Find(entry.FactoryKey);
            if (factory == null)
                throw new InvalidOperationException($"页面「{entry.Id}」缺少工厂绑定。请从清单同步生成脚本。");
            return new PageRoute(entry, factory);
        }

        /// <summary>获取或创建指定导航层对应的画布根节点。</summary>
        /// <param name="layer">页面显示层级。</param>
        /// <returns>指定层的布局根节点。</returns>
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

        #endregion
    }
}
