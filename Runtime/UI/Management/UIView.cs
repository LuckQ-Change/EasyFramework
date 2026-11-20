using System;
using System.Threading.Tasks;

namespace EasyFramework.UI
{
    public abstract class UIView : UIObject
    {
        protected new UIDisplay Display => (UIDisplay)base.Display;
        public bool IsOpen => Display != null && !Display.IsClosing;
        public Task CloseAsync() => Display == null ? Task.CompletedTask : Display.CloseAsync();
        internal abstract void SetArguments(object args);
        internal abstract void RefreshView();
        internal virtual void CompleteResult() { }
    }

    public abstract class UIView<TArgs> : UIView
    {
        internal sealed override void SetArguments(object args) => Args = (TArgs)args;
        internal sealed override void RefreshView() => Render(() => Refresh(Args));

        protected TArgs Args { get; private set; }
        protected virtual void Refresh(TArgs args) { }
    }

    public readonly struct UIResult<T>
    {
        public bool Confirmed { get; }
        public T Value { get; }

        internal UIResult(T value)
        {
            Confirmed = true;
            Value = value;
        }
    }

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
