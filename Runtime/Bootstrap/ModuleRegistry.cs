using System;
using System.Collections.Generic;
using EasyFramework.Tweening;

namespace EasyFramework
{
    /// <summary>注册默认模块及业务扩展；晚到注册作为完整批次安装。</summary>
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

        /// <summary>只移除指定注册器，避免演示场景关闭时清除项目的其他注册器。</summary>
        public static bool RemoveRegistrar(Action<ModuleManager> registrar) =>
            registrar != null && _extraRegistrars.Remove(registrar);

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
            modules.Register<TweenModule>();
        }
    }
}
