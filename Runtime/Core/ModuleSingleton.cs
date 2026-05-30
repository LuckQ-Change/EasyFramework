using System;

namespace EasyFramework
{
    public abstract class ModuleSingleton<T> : IModule where T : ModuleSingleton<T>, new()
    {
        public static T Instance { get; private set; }

        protected ModuleSingleton()
        {
            if (Instance != null)
            {
                throw new InvalidOperationException(
                    $"[{typeof(T).Name}] singleton already exists. Use {typeof(T).Name}.Instance instead of new.");
            }
            Instance = (T)this;
        }

        void IModule.OnInit() => OnInit();
        void IModule.OnUpdate(float deltaTime) => OnUpdate(deltaTime);

        void IModule.OnShutdown()
        {
            try { OnShutdown(); }
            finally { Instance = null; }
        }

        protected virtual void OnInit() { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnShutdown() { }
    }
}
