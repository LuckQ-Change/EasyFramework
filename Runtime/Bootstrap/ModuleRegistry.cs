using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>登记项目模块。启动完成后，新注册器立即作为独立批次安装。</summary>
    public static class ModuleRegistry
    {
        private static readonly List<Action<ModuleManager>> Registrars = new();

        public static async Task<bool> AddRegistrarAsync(Action<ModuleManager> registrar)
        {
            if (registrar == null) throw new ArgumentNullException(nameof(registrar));
            if (Registrars.Contains(registrar)) return false;
            Registrars.Add(registrar);
            try
            {
                if (ModuleManager.Current is ModuleManager modules)
                    await modules.InstallAsync(registrar);
                return true;
            }
            catch
            {
                Registrars.Remove(registrar);
                throw;
            }
        }

        public static void RemoveRegistrar(Action<ModuleManager> registrar) => Registrars.Remove(registrar);

        internal static void RegisterAll(ModuleManager modules)
        {
            modules.Register<EventModule>();
            modules.Register<TimerModule>();
            modules.Register<PoolModule>();
            modules.Register<TweenModule>();
            foreach (var registrar in Registrars.ToArray()) registrar(modules);
            modules.Register<AudioModule>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (ModuleManager.Current is ModuleManager modules) _ = modules.StopAsync();
            Registrars.Clear();
        }
    }
}
