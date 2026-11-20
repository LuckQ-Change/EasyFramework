using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    public static class UIService
    {
        public static Task<TView> OpenAsync<TView, TArgs>(TArgs args, CancellationToken token = default)
            where TView : UIView<TArgs>, new() =>
            Manager().OpenAsync<TView>(args, UIPageKind.Screen, token);

        public static async Task<UIResult<TResult>> ShowAsync<TView, TArgs, TResult>(TArgs args, CancellationToken token = default)
            where TView : UIPopup<TArgs, TResult>, new()
        {
            var result = new TaskCompletionSource<UIResult<TResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TView view = await Manager().OpenAsync<TView>(args, UIPageKind.Popup, token, popup => popup.BeginRequest(result));
            if (!token.CanBeCanceled || result.Task.IsCompleted) return await result.Task;

            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => canceled.TrySetResult(true));
            if (await Task.WhenAny(result.Task, canceled.Task) == canceled.Task)
            {
                if (view.OwnsRequest(result)) await view.CloseAsync();
                token.ThrowIfCancellationRequested();
            }
            return await result.Task;
        }

        public static TView Get<TView>() where TView : UIView => UIManager.Current?.Get<TView>();
        public static Task CloseAsync<TView>() where TView : UIView =>
            Get<TView>()?.CloseAsync() ?? Task.CompletedTask;
        public static Task BackAsync() => UIManager.Current?.BackAsync() ?? Task.CompletedTask;

        public static Task WarmAsync<TView>(CancellationToken token = default) where TView : UIView =>
            UIManager.RequireAssets().WarmAsync<GameObject>(UIManager.AddressOf(typeof(TView)), token);

        private static UIManager Manager() =>
            UIManager.Instance ?? throw new InvalidOperationException("UI 正在关闭。");
    }
}
