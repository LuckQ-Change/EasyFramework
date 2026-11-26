using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>由模块管理器按阶段启动、按逆序关闭。</summary>
    public abstract class Module
    {
        private ModuleManager _owner;

        /// <summary>本模块所属的宿主，模块间依赖应从此处显式获取。</summary>
        protected ModuleManager Host => _owner ?? throw new InvalidOperationException("模块尚未装配到宿主。");

        internal void BindOwner(ModuleManager owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (Interlocked.CompareExchange(ref _owner, owner, null) != null)
                throw new InvalidOperationException($"模块 {GetType().Name} 已装配到宿主。");
        }

        internal Task InitializeAsync(CancellationToken token)
        {
            OnInit();
            return OnInitAsync(token);
        }

        internal async Task ShutdownAsync()
        {
            try { await OnShutdownAsync(); }
            finally { OnShutdown(); }
        }

        protected virtual void OnInit() { }
        protected virtual Task OnInitAsync(CancellationToken token) => Task.CompletedTask;
        protected virtual Task OnShutdownAsync() => Task.CompletedTask;
        protected virtual void OnShutdown() { }
    }

    /// <summary>提供所属作用域内的强类型模块查询。</summary>
    public abstract class Module<T> : Module where T : Module<T>
    {
        /// <summary>当前回调作用域内的模块实例，作用域外使用显式设定的默认宿主。</summary>
        public static T Instance => ModuleManager.Current?.Get<T>();
    }
}
