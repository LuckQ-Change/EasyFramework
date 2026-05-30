using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public static class ModuleRegistry
    {
        private static readonly List<Action<ModuleManager>> _extraRegistrars = new List<Action<ModuleManager>>();

        public static void AddRegistrar(Action<ModuleManager> registrar)
        {
            if (registrar == null) return;
            _extraRegistrars.Add(registrar);
        }

        public static void ClearRegistrars()
        {
            _extraRegistrars.Clear();
        }

        public static void ApplyAll(ModuleManager modules)
        {
            if (modules == null) return;
            RegisterDefault(modules);
            for (int i = 0; i < _extraRegistrars.Count; i++)
            {
                try { _extraRegistrars[i](modules); }
                catch (Exception ex) { Log.Error($"[ModuleRegistry] registrar #{i} error: {ex}"); }
            }
        }

        public static void RegisterDefault(ModuleManager modules)
        {
            if (modules == null) return;
            modules.Register<EventModule>();
            modules.Register<TimerModule>();
            modules.Register<PoolModule>();
            modules.Register<AssetModule>();
            modules.Register<NetworkModule>();
            modules.Register<HotfixModule>();
        }
    }
}
