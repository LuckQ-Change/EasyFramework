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
            if (_extraRegistrars.Contains(registrar)) return;
            _extraRegistrars.Add(registrar);

            if (Entry.IsRunning)
            {
                try
                {
                    Entry.Install(registrar);
                }
                catch
                {
                    _extraRegistrars.Remove(registrar);
                    throw;
                }
            }
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
                _extraRegistrars[i](modules);
            }
        }

        public static void RegisterDefault(ModuleManager modules)
        {
            if (modules == null) return;
            modules.Register<EventModule>();
            modules.Register<TimerModule>();
            modules.Register<PoolModule>();
        }
    }
}
