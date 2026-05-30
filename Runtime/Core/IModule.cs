namespace EasyFramework
{
    public interface IModule
    {
        void OnInit();
        void OnUpdate(float deltaTime);
        void OnShutdown();
    }
}
