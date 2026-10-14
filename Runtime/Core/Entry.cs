using System;

namespace EasyFramework
{
    /// <summary>提供框架启动、模块安装、逐帧更新和关闭的公共入口。</summary>
    public static class Entry
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
