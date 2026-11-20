using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>由模块管理器按阶段启动、按逆序关闭。</summary>
    public abstract class Module
    {
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

    /// <summary>通过当前模块管理器访问已注册的实例。</summary>
    public abstract class Module<T> : Module where T : Module<T>
    {
        public static T Instance => ModuleManager.Current?.Get<T>();
    }

    /// <summary>仅需要逐帧更新的模块实现此接口。</summary>
    public interface IFrameUpdate
    {
        void Tick(float deltaTime);
    }
}
