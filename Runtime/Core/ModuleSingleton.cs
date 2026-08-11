using System;

namespace EasyFramework
{
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
        }

        internal sealed override void UnregisterInstance()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
