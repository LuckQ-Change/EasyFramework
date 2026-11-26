using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>管理独立宿主的模块实例、就绪状态、逐帧更新和逆序关闭。</summary>
    public sealed class ModuleManager
    {
        /// <summary>记录已进入初始化的模块，关闭时使用同一注册键。</summary>
        private sealed class StartedModule
        {
            internal readonly Type Type;
            internal readonly Module Module;

            internal StartedModule(Type type, Module module)
            {
                Type = type;
                Module = module;
            }
        }

        /// <summary>记录当前异步调用链所属的宿主及生命周期状态。</summary>
        private sealed class HostScope : IDisposable
        {
            internal readonly ModuleManager Host;
            internal readonly HostScope Previous;
            internal readonly bool IsBatch;
            internal volatile bool IsDisposed;

            internal HostScope(ModuleManager host, bool isBatch)
            {
                Host = host;
                IsBatch = isBatch;
                Previous = _ambient.Value;
                _ambient.Value = this;
            }

            /// <summary>恢复外层的宿主作用域。</summary>
            public void Dispose()
            {
                if (IsDisposed) return;
                if (!ReferenceEquals(_ambient.Value, this))
                    throw new InvalidOperationException("宿主作用域必须按进入的相反顺序退出。");
                _ambient.Value = Previous;
                IsDisposed = true;
            }
        }

        private static readonly AsyncLocal<HostScope> _ambient = new();
        private static ModuleManager _default;

        [ThreadStatic] private static ModuleManager _frameHost;
        [ThreadStatic] private static HostScope _frameEntryScope;

        private readonly object _lifecycleGate = new();
        private readonly Dictionary<Type, Module> _readyModules = new();
        private readonly List<StartedModule> _started = new();
        private readonly SemaphoreSlim _batchGate = new(1);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly CancellationToken _shutdownToken;
        private readonly bool _usesDefault;
        private IFrameUpdate[] _updates = Array.Empty<IFrameUpdate>();
        private TaskCompletionSource<bool> _stop;
        private TaskCompletionSource<bool> _tickIdle;
        private bool _hasStarted;
        private bool _isTicking;
        private volatile bool _isRunning;

        /// <summary>创建使用独立模块集合的宿主。</summary>
        /// <param name="useDefault">是否将此宿主提供给作用域外的模块查询。</param>
        /// <param name="runtimeMode">该宿主独立使用的运行环境。</param>
        public ModuleManager(bool useDefault = false, RuntimeMode runtimeMode = EasyFramework.RuntimeMode.Editor)
        {
            _usesDefault = useDefault;
            RuntimeMode = runtimeMode;
            _shutdownToken = _shutdown.Token;
        }

        /// <summary>为全局模块查询显式指定的默认宿主。</summary>
        public static ModuleManager Default => Volatile.Read(ref _default);

        /// <summary>当前调用所属宿主。没有作用域时读取显式指定的默认宿主。</summary>
        public static ModuleManager Current
        {
            get
            {
                HostScope scope = _ambient.Value;
                if (_frameHost != null && ReferenceEquals(scope, _frameEntryScope)) return _frameHost;
                return scope?.Host ?? Default;
            }
        }

        /// <summary>此宿主使用的运行环境。</summary>
        public RuntimeMode RuntimeMode { get; }

        /// <summary>宿主是否已完成启动并允许帧更新。</summary>
        public bool IsRunning => _isRunning;

        /// <summary>宿主已经就绪的模块数量。</summary>
        public int Count
        {
            get
            {
                lock (_readyModules) return _readyModules.Count;
            }
        }

        #region 公共方法

        /// <summary>检查并启动显式装配；检查通过前不会调用任何模块工厂。</summary>
        /// <param name="composition">模块工厂和依赖声明。</param>
        /// <returns>整份装配完成的任务。</returns>
        public Task StartAsync(ModuleComposition composition) => StartAsync(composition, CancellationToken.None);

        /// <summary>检查并启动可取消的显式装配。</summary>
        /// <param name="composition">模块工厂和依赖声明。</param>
        /// <param name="cancellationToken">取消本次启动的令牌。</param>
        /// <returns>整份装配完成的任务。</returns>
        public Task StartAsync(ModuleComposition composition, CancellationToken cancellationToken)
        {
            if (composition == null) throw new ArgumentNullException(nameof(composition));
            cancellationToken.ThrowIfCancellationRequested();
            List<List<ModuleRegistration>> plan = ModuleStartupPlan.Build(composition.CopyRegistrations());
            BeginStart();
            return RunStartupAsync(plan, cancellationToken);
        }

        /// <summary>在此宿主中查找已就绪的指定模块。</summary>
        /// <typeparam name="T">模块的注册类型。</typeparam>
        /// <returns>已就绪的模块，缺失时返回空。</returns>
        public T Get<T>() where T : Module
        {
            lock (_readyModules) return _readyModules.TryGetValue(typeof(T), out Module module) ? (T)module : null;
        }

        /// <summary>取得此宿主中必需的模块。</summary>
        /// <typeparam name="T">模块的注册类型。</typeparam>
        /// <returns>当前宿主的模块实例。没有该模块时抛出异常。</returns>
        public T Require<T>() where T : Module =>
            Get<T>() ?? throw new InvalidOperationException($"当前宿主的模块 {typeof(T).FullName} 尚未就绪。");

        /// <summary>在当前调用链中指定模块查询的宿主，可跨 await 使用。</summary>
        /// <returns>按进入的相反顺序释放的作用域句柄。</returns>
        public IDisposable EnterScope() => new HostScope(this, isBatch: false);

        internal bool IsInsideLifecycleCallback => IsInsideBatch();

        /// <summary>按模块启动顺序推进此宿主的帧更新。</summary>
        /// <param name="frameTime">供本次帧更新共用的缩放与非缩放秒数。</param>
        public void Tick(FrameTime frameTime)
        {
            lock (_lifecycleGate)
            {
                if (!_isRunning || _isTicking) return;
                _isTicking = true;
            }

            // 帧回调同步执行。使用线程内作用域，避免每帧写 AsyncLocal 所产生的分配。
            ModuleManager previousHost = _frameHost;
            HostScope previousScope = _frameEntryScope;
            _frameHost = this;
            _frameEntryScope = _ambient.Value;
            try
            {
                IFrameUpdate[] updates = Volatile.Read(ref _updates);
                foreach (IFrameUpdate update in updates)
                {
                    if (!_isRunning) break;
                    try { update.Tick(frameTime); }
                    catch (Exception exception) { Log.Error($"[Modules] {update.GetType().Name}.Tick 执行失败。", exception); }
                }
            }
            finally
            {
                _frameHost = previousHost;
                _frameEntryScope = previousScope;
                lock (_lifecycleGate)
                {
                    _isTicking = false;
                    _tickIdle?.TrySetResult(true);
                    _tickIdle = null;
                }
            }
        }

        /// <summary>停止此宿主，等待在途初始化收尾后逆序释放模块。</summary>
        /// <returns>此宿主关闭完成的任务，可从外部重复调用。</returns>
        public Task StopAsync()
        {
            if (IsInsideBatch()) throw new InvalidOperationException("不能在同一宿主的启动或关闭回调中等待关闭。");

            Task frameEnded;
            TaskCompletionSource<bool> completion;
            lock (_lifecycleGate)
            {
                if (_stop != null) return _stop.Task;
                if (!_hasStarted) return Task.CompletedTask;

                completion = _stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _isRunning = false;
                if (_isTicking) _tickIdle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                frameEnded = _tickIdle?.Task ?? Task.CompletedTask;
            }

            Cancel(_shutdown);
            _ = FinishStopAsync(frameEnded, completion);
            return completion.Task;
        }

        #endregion

        #region 私有逻辑

        private void BeginStart()
        {
            lock (_lifecycleGate)
            {
                if (_hasStarted) throw new InvalidOperationException("宿主实例只能启动一次。");
                if (_usesDefault && Interlocked.CompareExchange(ref _default, this, null) != null)
                    throw new InvalidOperationException("已有其他模块宿主占用默认作用域。");

                _hasStarted = true;
            }
        }

        private async Task RunStartupAsync(List<List<ModuleRegistration>> plan, CancellationToken cancellationToken)
        {
            await _batchGate.WaitAsync(_shutdownToken);
            using var scope = new HostScope(this, isBatch: true);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken, cancellationToken);
            var started = new List<StartedModule>();
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                foreach (List<ModuleRegistration> stage in plan)
                {
                    var tasks = new List<Task<StartedModule>>(stage.Count);
                    foreach (ModuleRegistration registration in stage)
                    {
                        if (cancellation.IsCancellationRequested) break;
                        tasks.Add(InitializeAsync(registration, started, cancellation));
                    }

                    // 等同阶段已经进入初始化的任务全部退出，再发布就绪实例或按逆序回滚。
                    StartedModule[] ready = await Task.WhenAll(tasks);
                    cancellation.Token.ThrowIfCancellationRequested();
                    lock (_readyModules)
                        foreach (StartedModule entry in ready) _readyModules.Add(entry.Type, entry.Module);
                }

                lock (_lifecycleGate)
                {
                    if (_stop != null) throw new OperationCanceledException(_shutdownToken);
                    cancellation.Token.ThrowIfCancellationRequested();
                    _started.AddRange(started);
                    RefreshUpdates();
                    _isRunning = true;
                }
            }
            catch
            {
                Cancel(cancellation);
                await ShutdownAsync(started);
                Interlocked.CompareExchange(ref _default, null, this);
                throw;
            }
            finally
            {
                _batchGate.Release();
            }
        }

        private async Task<StartedModule> InitializeAsync(ModuleRegistration registration, List<StartedModule> started,
            CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                Module module = registration.Factory(this) ??
                                throw new InvalidOperationException($"模块 {registration.Type.FullName} 的工厂返回空实例。");
                token.ThrowIfCancellationRequested();
                module.BindOwner(this);

                var entry = new StartedModule(registration.Type, module);
                started.Add(entry);
                await module.InitializeAsync(token);
                return entry;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Cancel(cancellation);
                throw new InvalidOperationException($"无法初始化模块 {registration.Type.FullName}。", exception);
            }
        }

        private async Task FinishStopAsync(Task frameEnded, TaskCompletionSource<bool> completion)
        {
            await frameEnded;
            await _batchGate.WaitAsync();
            using var scope = new HostScope(this, isBatch: true);
            try
            {
                await ShutdownAsync(_started);
                _started.Clear();
                Volatile.Write(ref _updates, Array.Empty<IFrameUpdate>());
            }
            finally
            {
                Interlocked.CompareExchange(ref _default, null, this);
                _shutdown.Dispose();
                _batchGate.Release();
                completion.TrySetResult(true);
            }
        }

        private async Task ShutdownAsync(List<StartedModule> started)
        {
            for (int i = started.Count - 1; i >= 0; i--)
            {
                StartedModule entry = started[i];
                try { await entry.Module.ShutdownAsync(); }
                catch (Exception exception) { Log.Error($"[Modules] {entry.Type.Name}.Shutdown 执行失败。", exception); }
                finally
                {
                    lock (_readyModules) _readyModules.Remove(entry.Type);
                }
            }
        }

        private void RefreshUpdates()
        {
            var updates = new List<IFrameUpdate>();
            foreach (StartedModule entry in _started)
                if (entry.Module is IFrameUpdate update) updates.Add(update);
            Volatile.Write(ref _updates, updates.ToArray());
        }

        private bool IsInsideBatch()
        {
            for (HostScope scope = _ambient.Value; scope != null; scope = scope.Previous)
                if (!scope.IsDisposed && scope.IsBatch && ReferenceEquals(scope.Host, this)) return true;
            return false;
        }

        private static void Cancel(CancellationTokenSource cancellation)
        {
            try { cancellation.Cancel(); }
            catch (Exception exception) { Log.Error("[Modules] 取消模块任务失败。", exception); }
        }

        #endregion
    }
}
