using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>
    /// UI 的常用入口。业务代码优先使用此类；只有自定义 UI 基础设施时才需要直接访问 UIManager。
    /// </summary>
    public static class UIService
    {
        /// <summary>底层管理器，供预加载、层级根节点等高级功能使用。</summary>
        public static UIManager Manager => UIManager.Instance;

        /// <summary>使用 View 上的 UIPrefab 地址打开页面。</summary>
        public static Task<TView> OpenAsync<TView>(
            object args = null,
            UILayer? layer = null,
            CancellationToken cancellationToken = default(CancellationToken))
            where TView : UIView =>
            RequireManager().OpenGeneratedAsync<TView>(args, layer, cancellationToken);

        /// <summary>使用显式资源地址打开页面，适用于动态地址等高级场景。</summary>
        public static Task<TView> OpenAtAsync<TView>(
            string location,
            object args = null,
            UILayer? layer = null,
            CancellationToken cancellationToken = default(CancellationToken))
            where TView : UIView =>
            RequireManager().OpenAsync<TView>(location, args, layer, cancellationToken);

        /// <summary>直接从 Prefab 打开页面，主要用于编辑器工具、测试或动态 Prefab。</summary>
        public static TView Open<TView>(GameObject prefab, object args = null, UILayer? layer = null)
            where TView : UIView => RequireManager().Open<TView>(prefab, args, layer);

        public static TView Get<TView>() where TView : UIView =>
            UIManager.Current?.Get<TView>();

        public static bool TryGet<TView>(out TView view) where TView : UIView
        {
            UIManager manager = UIManager.Current;
            if (manager != null) return manager.TryGet(out view);
            view = null;
            return false;
        }

        public static bool IsOpen<TView>() where TView : UIView => Get<TView>() != null;

        public static bool Close(UIView view) =>
            view != null && UIManager.Current != null && UIManager.Current.Close(view);

        public static bool Close<TView>() where TView : UIView =>
            UIManager.Current != null && UIManager.Current.Close<TView>();

        public static bool Back() =>
            UIManager.Current != null && UIManager.Current.Back();

        public static bool CloseTop() =>
            UIManager.Current != null && UIManager.Current.CloseTop();

        public static void CloseAll() => UIManager.Current?.CloseAll();

        private static UIManager RequireManager() =>
            UIManager.Instance ?? throw new InvalidOperationException("UI is shutting down.");
    }
}
