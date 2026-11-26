using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>提供默认 UI 宿主的开页、导航与资源预热入口。</summary>
    public static class UIService
    {
        /// <summary>打开或复用一个主界面，并刷新其参数。</summary>
        /// <typeparam name="TView">主界面 View 类型。</typeparam>
        /// <typeparam name="TArgs">主界面接收的参数类型。</typeparam>
        /// <param name="args">主界面的开页参数。</param>
        /// <param name="token">取消本次开页的令牌。</param>
        /// <returns>已打开的页面实例。</returns>
        public static Task<TView> OpenAsync<TView, TArgs>(TArgs args, CancellationToken token = default)
            where TView : UIView<TArgs> =>
            Manager().OpenAsync<TView>(args, UIPageKind.Screen, token);

        /// <summary>根据清单中的页面 ID 打开或复用主界面或弹窗。</summary>
        /// <param name="pageId">清单中登记的页面 ID。</param>
        /// <param name="args">页面的开页参数，省略时使用该页面参数类型的默认值。</param>
        /// <param name="token">取消本次开页的令牌。</param>
        /// <returns>已打开的页面实例。需要等待弹窗结果时使用强类型弹窗入口。</returns>
        public static Task<UIView> OpenAsync(string pageId, object args = null, CancellationToken token = default) =>
            Manager().OpenAsync(pageId, args, token);

        /// <summary>打开或复用弹窗，等待弹窗返回确认或取消结果。</summary>
        /// <typeparam name="TView">弹窗 View 类型。</typeparam>
        /// <typeparam name="TArgs">弹窗接收的参数类型。</typeparam>
        /// <typeparam name="TResult">弹窗确认时返回的值类型。</typeparam>
        /// <param name="args">弹窗的开页参数。</param>
        /// <param name="token">取消本次弹窗请求的令牌。</param>
        /// <returns>弹窗关闭时返回的确认结果。</returns>
        public static async Task<UIResult<TResult>> ShowAsync<TView, TArgs, TResult>(TArgs args, CancellationToken token = default)
            where TView : UIPopup<TArgs, TResult>
        {
            var result = new TaskCompletionSource<UIResult<TResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TView view = await Manager().OpenAsync<TView>(args, UIPageKind.Popup, token, popup => popup.BeginRequest(result));
            if (!token.CanBeCanceled || result.Task.IsCompleted) return await result.Task;

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
            // 单线程 Player 没有可执行 WhenAny 完成通知的线程池。
            while (!result.Task.IsCompleted && !token.IsCancellationRequested)
                await Task.Yield();
            if (!result.Task.IsCompleted)
#else
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => canceled.TrySetResult(true));
            if (await Task.WhenAny(result.Task, canceled.Task) == canceled.Task)
#endif
            {
                if (view.OwnsRequest(result)) await view.CloseAsync();
                token.ThrowIfCancellationRequested();
            }
            return await result.Task;
        }

        /// <summary>按 View 类型查询已打开的页面。</summary>
        /// <typeparam name="TView">页面 View 类型。</typeparam>
        /// <returns>匹配的页面实例，未打开时为空。</returns>
        public static TView Get<TView>() where TView : UIView => UIManager.Current?.Get<TView>();

        /// <summary>按 View 类型关闭页面。</summary>
        /// <typeparam name="TView">页面 View 类型。</typeparam>
        /// <returns>页面关闭完成的任务。</returns>
        public static Task CloseAsync<TView>() where TView : UIView =>
            Get<TView>()?.CloseAsync() ?? Task.CompletedTask;

        /// <summary>按照当前顶层页面的导航规则执行返回。</summary>
        /// <returns>当前返回操作完成的任务。</returns>
        public static Task BackAsync() => UIManager.Current?.BackAsync() ?? Task.CompletedTask;

        /// <summary>按 View 类型预热页面 Prefab。</summary>
        /// <typeparam name="TView">页面 View 类型。</typeparam>
        /// <param name="token">取消本次预热的令牌。</param>
        /// <returns>页面资源预热完成的任务。</returns>
        public static Task WarmAsync<TView>(CancellationToken token = default) where TView : UIView
        {
            string address = UIManager.AddressOf(typeof(TView));
            return UIManager.RequireAssets().WarmAsync<GameObject>(address, token);
        }

        /// <summary>按页面 ID 预热清单登记的 Prefab。</summary>
        /// <param name="pageId">清单中登记的页面 ID。</param>
        /// <param name="token">取消本次预热的令牌。</param>
        /// <returns>页面资源预热完成的任务。</returns>
        public static Task WarmAsync(string pageId, CancellationToken token = default)
        {
            string address = UIManager.AddressOf(pageId);
            return UIManager.RequireAssets().WarmAsync<GameObject>(address, token);
        }

        private static UIManager Manager()
        {
            if (!UIManager.HasDefaultPresentationScope)
                throw new InvalidOperationException("隔离宿主需要自己的 UI 入口，默认 UI 服务不可用。");
            return UIManager.Current ?? throw new InvalidOperationException("UI 宿主不可用。请在 Startup Config 中启用 UI。");
        }
    }
}
