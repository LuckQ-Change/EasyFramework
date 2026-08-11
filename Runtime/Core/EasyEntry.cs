using System;

namespace EasyFramework
{
    public static class EasyEntry
    {
        public static ModuleManager Modules { get; private set; }
        public static bool IsRunning { get; private set; }

        public static void Init(Action<ModuleManager> configure = null)
        {
            if (IsRunning) return;
            if (Modules != null)
            {
                throw new InvalidOperationException("EasyFramework is already initializing.");
            }

            var modules = new ModuleManager();
            Modules = modules;
            try
            {
                configure?.Invoke(modules);
                modules.InitAll();
                IsRunning = true;
                Log.Info("EasyFramework started.");
            }
            catch
            {
                try { modules.ShutdownAll(); }
                finally
                {
                    Modules = null;
                    IsRunning = false;
                }
                throw;
            }
        }

        public static void Install(Action<ModuleManager> installer)
        {
            if (!IsRunning || Modules == null)
            {
                throw new InvalidOperationException("EasyFramework is not running.");
            }
            Modules.Install(installer);
        }

        public static void Update(float deltaTime)
        {
            if (!IsRunning) return;
            Modules.Update(deltaTime);
        }

        public static void Shutdown()
        {
            var modules = Modules;
            if (modules == null) return;

            IsRunning = false;
            try
            {
                modules.ShutdownAll();
            }
            finally
            {
                Modules = null;
                Log.Info("EasyFramework shutdown.");
            }
        }
    }
}
