using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class ModuleManager
    {
        private readonly Dictionary<Type, IModule> _modules = new Dictionary<Type, IModule>();
        private readonly List<IModule> _updateList = new List<IModule>();

        public ModuleManagerState State { get; private set; } = ModuleManagerState.Registering;
        public int Count => _updateList.Count;

        public T Register<T>() where T : class, IModule, new()
        {
            if (State != ModuleManagerState.Registering && State != ModuleManagerState.Installing)
            {
                throw new InvalidOperationException(
                    $"Cannot register {typeof(T).Name} while ModuleManager is {State}.");
            }

            var type = typeof(T);
            if (_modules.TryGetValue(type, out var exist))
            {
                return (T)exist;
            }

            var module = new T();
            try
            {
                (module as ModuleSingletonBase)?.RegisterInstance();
                _modules.Add(type, module);
                _updateList.Add(module);
                return module;
            }
            catch
            {
                (module as ModuleSingletonBase)?.UnregisterInstance();
                throw;
            }
        }

        public T Get<T>() where T : class, IModule
        {
            return _modules.TryGetValue(typeof(T), out var module) ? module as T : null;
        }

        public void InitAll()
        {
            if (State != ModuleManagerState.Registering)
            {
                throw new InvalidOperationException($"Cannot initialize ModuleManager while it is {State}.");
            }

            State = ModuleManagerState.Initializing;
            for (int i = 0; i < _updateList.Count; i++)
            {
                try
                {
                    _updateList[i].OnInit();
                }
                catch (Exception ex)
                {
                    string moduleName = _updateList[i].GetType().Name;
                    RollbackInitialization(i);
                    State = ModuleManagerState.Faulted;
                    throw new InvalidOperationException(
                        $"Module {moduleName} initialization failed at index {i}.", ex);
                }
            }
            State = ModuleManagerState.Running;
        }

        public void Install(Action<ModuleManager> installer)
        {
            if (installer == null) return;

            if (State == ModuleManagerState.Registering)
            {
                installer(this);
                return;
            }
            if (State != ModuleManagerState.Running)
            {
                throw new InvalidOperationException($"Cannot install modules while ModuleManager is {State}.");
            }

            int startIndex = _updateList.Count;
            State = ModuleManagerState.Installing;
            try
            {
                installer(this);
                State = ModuleManagerState.Initializing;
                for (int i = startIndex; i < _updateList.Count; i++)
                {
                    try
                    {
                        _updateList[i].OnInit();
                    }
                    catch
                    {
                        RollbackInstalledModules(startIndex, i);
                        throw;
                    }
                }
                State = ModuleManagerState.Running;
            }
            catch
            {
                if (_updateList.Count > startIndex)
                {
                    RemoveRegisteredRange(startIndex);
                }
                State = ModuleManagerState.Running;
                throw;
            }
        }

        public void Update(float deltaTime)
        {
            if (State != ModuleManagerState.Running) return;
            for (int i = 0; i < _updateList.Count; i++)
            {
                try
                {
                    _updateList[i].OnUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    TryLogError($"[ModuleManager] {_updateList[i].GetType().Name}.OnUpdate failed: {ex}");
                }
            }
        }

        public void ShutdownAll()
        {
            if (State == ModuleManagerState.Stopped || State == ModuleManagerState.ShuttingDown) return;

            if (State == ModuleManagerState.Registering || State == ModuleManagerState.Installing)
            {
                RemoveRegisteredRange(0);
                State = ModuleManagerState.Stopped;
                return;
            }

            State = ModuleManagerState.ShuttingDown;
            for (int i = _updateList.Count - 1; i >= 0; i--)
            {
                SafeShutdown(_updateList[i]);
            }
            RemoveRegisteredRange(0);
            State = ModuleManagerState.Stopped;
        }

        private void RollbackInitialization(int failedIndex)
        {
            for (int i = failedIndex; i >= 0; i--)
            {
                SafeShutdown(_updateList[i]);
            }
            RemoveRegisteredRange(0);
        }

        private void RollbackInstalledModules(int startIndex, int failedIndex)
        {
            for (int i = failedIndex; i >= startIndex; i--)
            {
                SafeShutdown(_updateList[i]);
            }
        }

        private void RemoveRegisteredRange(int startIndex)
        {
            for (int i = _updateList.Count - 1; i >= startIndex; i--)
            {
                var module = _updateList[i];
                _modules.Remove(module.GetType());
                (module as ModuleSingletonBase)?.UnregisterInstance();
                _updateList.RemoveAt(i);
            }
        }

        private static void SafeShutdown(IModule module)
        {
            try
            {
                module.OnShutdown();
            }
            catch (Exception ex)
            {
                TryLogError($"[ModuleManager] {module.GetType().Name}.OnShutdown failed: {ex}");
            }
            finally
            {
                (module as ModuleSingletonBase)?.UnregisterInstance();
            }
        }

        private static void TryLogError(string message)
        {
            try { Log.Error(message); }
            catch { }
        }
    }
}
