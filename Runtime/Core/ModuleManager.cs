using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>批量注册模块，管理初始化、逐帧更新以及关闭。</summary>
    public sealed class ModuleManager
    {
        private readonly Dictionary<Type, Module> _modules = new();
        private readonly List<(Module Module, int Order)> _registered = new();
        private readonly List<Module> _initialized = new();
        private readonly List<IFrameUpdate> _updates = new();
        private readonly SemaphoreSlim _batchGate = new(1);
        private readonly CancellationTokenSource _shutdown = new();
        private TaskCompletionSource<bool> _stop;
        private bool _used;
        private bool _registering;
        private bool _ticking;

        public static ModuleManager Current { get; private set; }
        public bool IsRunning { get; private set; }
        public int Count => _registered.Count;

        public Task StartAsync(Action<ModuleManager> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            if (Current != null || _used)
                throw new InvalidOperationException("A module manager is already active or this instance has been stopped.");

            _used = true;
            Current = this;
            return AddBatchAsync(configure, startup: true);
        }

        /// <summary>阶段从小到大初始化。同阶段的异步初始化可以重叠。</summary>
        public T Register<T>(int order = 0) where T : Module, new()
        {
            if (!_registering || _stop != null)
                throw new InvalidOperationException("Register modules in a registrar or an installation batch.");
            if (Get<T>() is T existing) return existing;

            var module = new T();
            _modules.Add(typeof(T), module);
            _registered.Add((module, order));
            return module;
        }

        public T Get<T>() where T : Module =>
            _modules.TryGetValue(typeof(T), out Module module) ? (T)module : null;

        public Task InstallAsync(Action<ModuleManager> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            if (Current != this || _stop != null)
                throw new InvalidOperationException("Module installation requires an active manager.");
            return AddBatchAsync(configure, startup: false);
        }

        public void Tick(float deltaTime)
        {
            if (!IsRunning || _ticking) return;
            _ticking = true;
            try
            {
                int count = _updates.Count;
                for (int i = 0; i < count && IsRunning; i++)
                {
                    IFrameUpdate update = _updates[i];
                    try { update.Tick(deltaTime); }
                    catch (Exception exception) { Log.Error($"[Modules] {update.GetType().Name}.Tick failed.", exception); }
                }
            }
            finally { _ticking = false; }
        }

        public Task StopAsync()
        {
            if (_stop != null) return _stop.Task;
            if (Current != this) return Task.CompletedTask;

            _stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsRunning = false;
            Cancel(_shutdown);
            _ = FinishStopAsync();
            return _stop.Task;
        }

        private async Task FinishStopAsync()
        {
            await _batchGate.WaitAsync();
            try
            {
                await RemoveBatchAsync(0, 0);
                _updates.Clear();
                Current = null;
                _shutdown.Dispose();
            }
            finally
            {
                _batchGate.Release();
                _stop.TrySetResult(true);
            }
        }

        private async Task AddBatchAsync(Action<ModuleManager> configure, bool startup)
        {
            CancellationToken stop = _shutdown.Token;
            await _batchGate.WaitAsync(stop);
            int start = _registered.Count;
            int initialized = _initialized.Count;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);
            try
            {
                stop.ThrowIfCancellationRequested();
                if (!startup && !IsRunning)
                    throw new InvalidOperationException("Module startup did not complete.");

                _registering = true;
                try { configure(this); }
                finally { _registering = false; }

                foreach (var phase in _registered.Skip(start).GroupBy(entry => entry.Order).OrderBy(group => group.Key))
                {
                    var tasks = new List<Task>();
                    foreach (var entry in phase) tasks.Add(InitializeAsync(entry.Module, cancellation));
                    await Task.WhenAll(tasks);
                }
                stop.ThrowIfCancellationRequested();
                for (int index = initialized; index < _initialized.Count; index++)
                    if (_initialized[index] is IFrameUpdate update) _updates.Add(update);
                IsRunning = true;
            }
            catch
            {
                await RemoveBatchAsync(start, initialized);
                if (startup && _stop == null) Current = null;
                throw;
            }
            finally { _batchGate.Release(); }
        }

        private async Task InitializeAsync(Module module, CancellationTokenSource cancellation)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            _initialized.Add(module);
            try { await module.InitializeAsync(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Cancel(cancellation);
                throw new InvalidOperationException($"Cannot initialize module {module.GetType().Name}.", exception);
            }
        }

        private static void Cancel(CancellationTokenSource cancellation)
        {
            try { cancellation.Cancel(); }
            catch (Exception exception) { Log.Error("[Modules] Startup cancellation failed.", exception); }
        }

        private async Task RemoveBatchAsync(int start, int initialized)
        {
            for (int i = _initialized.Count - 1; i >= initialized; i--)
            {
                Module module = _initialized[i];
                try { await module.ShutdownAsync(); }
                catch (Exception exception) { Log.Error($"[Modules] {module.GetType().Name}.Shutdown failed.", exception); }
            }
            _initialized.RemoveRange(initialized, _initialized.Count - initialized);
            for (int i = start; i < _registered.Count; i++) _modules.Remove(_registered[i].Module.GetType());
            _registered.RemoveRange(start, _registered.Count - start);
        }
    }
}
