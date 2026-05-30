using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class ModuleManager
    {
        private readonly Dictionary<Type, IModule> _modules = new Dictionary<Type, IModule>();
        private readonly List<IModule> _updateList = new List<IModule>();
        private bool _started;

        public T Register<T>() where T : class, IModule, new()
        {
            var type = typeof(T);
            if (_modules.TryGetValue(type, out var exist))
            {
                return (T)exist;
            }

            var module = new T();
            _modules.Add(type, module);
            _updateList.Add(module);

            if (_started)
            {
                module.OnInit();
            }
            return module;
        }

        public T Get<T>() where T : class, IModule
        {
            return _modules.TryGetValue(typeof(T), out var module) ? module as T : null;
        }

        public void InitAll()
        {
            if (_started) return;
            _started = true;
            for (int i = 0; i < _updateList.Count; i++)
            {
                _updateList[i].OnInit();
            }
        }

        public void Update(float deltaTime)
        {
            if (!_started) return;
            for (int i = 0; i < _updateList.Count; i++)
            {
                _updateList[i].OnUpdate(deltaTime);
            }
        }

        public void ShutdownAll()
        {
            if (!_started) return;
            for (int i = _updateList.Count - 1; i >= 0; i--)
            {
                _updateList[i].OnShutdown();
            }
            _modules.Clear();
            _updateList.Clear();
            _started = false;
        }
    }
}
