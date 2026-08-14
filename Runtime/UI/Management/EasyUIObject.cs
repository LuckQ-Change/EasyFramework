using System;

namespace EasyFramework.UI
{
    /// <summary>Base class for UI business objects. It has no Unity component lifetime.</summary>
    public abstract class EasyUIObject : IDisposable
    {
        public EasyUIDisplay Display { get; private set; }
        public EasyUIBinding Binding { get; private set; }
        public EasyUIManager Manager => Display == null ? null : Display.Manager;
        public bool IsDisposed { get; private set; }

        /// <summary>Override when bindings should read a separate ViewModel.</summary>
        public virtual object BindingSource => this;

        internal void Initialize(EasyUIDisplay display, EasyUIBinding binding)
        {
            Display = display ?? throw new ArgumentNullException(nameof(display));
            Binding = binding;
            OnCreated();
        }

        protected virtual void OnCreated() { }
        protected virtual void OnDispose() { }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { OnDispose(); }
            finally
            {
                Binding = null;
                Display = null;
            }
        }
    }

    /// <summary>Reusable pure C# item base. Derive Item, CostItem and other item families from it.</summary>
    public class EasyUIItem : EasyUIObject { }
}
