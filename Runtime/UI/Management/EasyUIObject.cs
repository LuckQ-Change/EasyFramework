using System;

namespace EasyFramework.UI
{
    /// <summary>UI 业务对象基类，不依赖 Unity 组件生命周期。</summary>
    public abstract class EasyUIObject : IDisposable
    {
        public EasyUIDisplay Display { get; private set; }
        public EasyUIBinding Binding { get; private set; }
        public EasyUIManager Manager => Display == null ? null : Display.Manager;
        public bool IsDisposed { get; private set; }

        /// <summary>绑定需要读取独立展示数据源时重写。</summary>
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
                try { ReleaseOwnedResources(); }
                finally
                {
                    Binding = null;
                    Display = null;
                }
            }
        }

        // 由运行时程序集统一释放框架资源，避免用户重写 OnDispose 时意外漏掉清理。
        private protected virtual void ReleaseOwnedResources() { }
    }

    /// <summary>可复用的纯 C# Item 基类，Item、CostItem 等业务条目可由此派生。</summary>
    public class EasyUIItem : EasyUIObject { }
}
