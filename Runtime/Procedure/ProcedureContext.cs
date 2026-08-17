using System;

namespace EasyFramework
{
    /// <summary>
    /// 同一流程中各节点共享的上下文基类。
    /// 项目可继承它并公开自己的 UI、服务或其他运行时状态。
    /// </summary>
    public class ProcedureContext : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            OnDispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>由业务上下文重写，用于释放自己持有的资源。</summary>
        protected virtual void OnDispose() { }
    }
}
