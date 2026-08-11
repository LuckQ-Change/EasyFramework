namespace EasyFramework
{
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

    public interface IModule
    {
        void OnInit();
        void OnUpdate(float deltaTime);
        void OnShutdown();
    }
}
