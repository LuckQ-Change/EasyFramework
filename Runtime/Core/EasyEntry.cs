namespace EasyFramework
{
    public static class EasyEntry
    {
        public static ModuleManager Modules { get; private set; }
        public static bool IsRunning { get; private set; }

        public static void Init()
        {
            if (IsRunning) return;
            Modules = new ModuleManager();
            Modules.InitAll();
            IsRunning = true;
            Log.Info("EasyFramework started.");
        }

        public static void Update(float deltaTime)
        {
            if (!IsRunning) return;
            Modules.Update(deltaTime);
        }

        public static void Shutdown()
        {
            if (!IsRunning) return;
            Modules.ShutdownAll();
            Modules = null;
            IsRunning = false;
            Log.Info("EasyFramework shutdown.");
        }
    }
}
