namespace EasyFramework.UI
{
    /// <summary>纯 C# View 业务基类，派生类型不能挂载到 Prefab。</summary>
    public class EasyUIView : EasyUIObject
    {
        public object OpenArgs { get; private set; }

        public void Close() => Display?.Close();
        public void BringToFront() => Display?.BringToFront();
        public void SetLayer(UILayer layer) => Display?.SetLayer(layer);

        internal void Open(object args)
        {
            OpenArgs = args;
            OnOpened(args);
        }

        internal void Focus()
        {
            OnFocused();
        }

        internal void Blur()
        {
            OnBlurred();
        }
        internal void CloseInternal()
        {
            OnClosed();
        }

        protected virtual void OnOpened(object args) { }
        protected virtual void OnFocused() { }
        protected virtual void OnBlurred() { }
        protected virtual void OnClosed() { }
    }

    /// <summary>强类型 View 基类，默认以 View 自身作为绑定源。</summary>
    public abstract class EasyUIView<TArgs> : EasyUIView
    {
        public TArgs Args { get; private set; }

        protected sealed override void OnOpened(object args)
        {
            if (args == null)
            {
                Args = default(TArgs);
            }
            else if (args is TArgs typed)
            {
                Args = typed;
            }
            else
            {
                throw new System.ArgumentException(
                    $"{GetType().Name} expects open args {typeof(TArgs).FullName}, received {args.GetType().FullName}.",
                    nameof(args));
            }
            OnOpened(Args);
        }

        protected virtual void OnOpened(TArgs args) { }
    }
}
