namespace EasyFramework
{
    /// <summary>表示模块管理器的注册、运行和收尾阶段。</summary>
    public enum ModuleManagerState
    {
        Registering,
        Initializing,
        Running,
        Installing,
        ShuttingDown,
        Stopped,
        Faulted,
    }

    /// <summary>定义由 ModuleManager 统一调度的模块生命周期。</summary>
    public interface IModule
    {
        void OnInit();
        void OnUpdate(float deltaTime);
        void OnShutdown();
    }
}
