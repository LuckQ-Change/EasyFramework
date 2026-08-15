namespace EasyFramework.UI
{
    /// <summary>Pure C# view business base. Never attach a derived type to a prefab.</summary>
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

    /// <summary>Strongly typed View base. The View itself is the default binding source.</summary>
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
