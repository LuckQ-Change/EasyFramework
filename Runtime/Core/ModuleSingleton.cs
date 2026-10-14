using System;

namespace EasyFramework
{
    /// <summary>把模块实例的建立与清理接到 IModule 生命周期。</summary>
    public abstract class ModuleSingletonBase : IModule
    {
        internal abstract void RegisterInstance();
        internal abstract void UnregisterInstance();

        void IModule.OnInit() => OnInit();
        void IModule.OnUpdate(float deltaTime) => OnUpdate(deltaTime);

        void IModule.OnShutdown()
        {
            try { OnShutdown(); }
            finally { UnregisterInstance(); }
        }

        protected virtual void OnInit() { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnShutdown() { }
    }

    /// <summary>为一种模块类型提供由 Manager 管理的静态 Instance。</summary>
    public abstract class ModuleSingleton<T> : ModuleSingletonBase
        where T : ModuleSingleton<T>, new()
    {
        public static T Instance { get; private set; }

        internal sealed override void RegisterInstance()
        {
            if (Instance != null)
            {
                throw new InvalidOperationException(
                    $"[{typeof(T).Name}] singleton already exists. Register modules through ModuleManager only.");
            }
            Instance = (T)this;
            Log.Info( $"[{typeof(T).Name}] Registered");
        }

        internal sealed override void UnregisterInstance()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
