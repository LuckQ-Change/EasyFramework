using System;
using System.Threading.Tasks;

namespace EasyFramework.UI
{
    /// <summary>可由 UI 宿主打开、关闭并持有控件绑定的页面逻辑。</summary>
    public abstract class UIView : UIObject
    {
        protected new UIDisplay Display => (UIDisplay)base.Display;
        internal bool HasDisplay => Display != null;

        /// <summary>页面实例是否已绑定并处于打开状态。</summary>
        public bool IsOpen => Display != null && !Display.IsClosing;

        /// <summary>关闭该页面并等待页面退场。</summary>
        /// <returns>页面关闭完成的任务。</returns>
        public Task CloseAsync() => Display == null ? Task.CompletedTask : Display.CloseAsync();
        internal abstract void SetArguments(object args);
        internal abstract void RefreshView();
        internal virtual void CompleteResult() { }
    }

    /// <summary>接收强类型开页参数并刷新内容的页面逻辑。</summary>
    public abstract class UIView<TArgs> : UIView
    {
        internal sealed override void SetArguments(object args) => Args = args == null ? default : (TArgs)args;
        internal sealed override void RefreshView() => Render(() => Refresh(Args));

        protected TArgs Args { get; private set; }
        protected virtual void Refresh(TArgs args) { }
    }

    /// <summary>弹窗关闭时返回的确认结果。</summary>
    public readonly struct UIResult<T>
    {
        /// <summary>是否由弹窗主动确认。</summary>
        public bool Confirmed { get; }
        /// <summary>弹窗确认时传回的数据。</summary>
        public T Value { get; }

        internal UIResult(T value)
        {
            Confirmed = true;
            Value = value;
        }
    }

    /// <summary>接收开页参数并可返回强类型确认结果的弹窗。</summary>
    public abstract class UIPopup<TArgs, TResult> : UIView<TArgs>
    {
        private TaskCompletionSource<UIResult<TResult>> _request;
        private UIResult<TResult> _result;

        internal void BeginRequest(TaskCompletionSource<UIResult<TResult>> request)
        {
            _request?.TrySetResult(default);
            _request = request;
            _result = default;
        }

        internal bool OwnsRequest(TaskCompletionSource<UIResult<TResult>> request) =>
            ReferenceEquals(_request, request);

        protected Task ConfirmAsync(TResult value)
        {
            if (Display != null && !Display.IsClosing)
                _result = new UIResult<TResult>(value);
            return CloseAsync();
        }

        internal override void CompleteResult()
        {
            _request?.TrySetResult(_result);
            _request = null;
        }
    }
}
