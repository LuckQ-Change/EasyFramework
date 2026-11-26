using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>管理当前流程和上下文，同一时间只允许执行一次切换。</summary>
    public sealed class ProcedureModule : Module<ProcedureModule>, IFrameUpdate
    {
        private static readonly AsyncLocal<TaskCompletionSource<bool>> _transitionScope = new();
        private readonly Dictionary<Type, ProcedureBase> _procedures = new();
        private readonly Dictionary<Type, Func<ProcedureBase>> _factories = new();
        private readonly CancellationTokenSource _shutdown = new();
        private Type _startupProcedureType;
        private ProcedureContext _context;
        private ProcedureBase _active;
        private TaskCompletionSource<bool> _transitionFinished;
        private bool _isCreatingProcedure;

        /// <summary>当前进入或正在运行的流程。</summary>
        public ProcedureBase ActiveProcedure => _active;
        /// <summary>当前流程的类型。</summary>
        public Type ActiveProcedureType => _active?.GetType();
        /// <summary>是否正在执行一次进入、退出或自动跳转。</summary>
        public bool IsTransitioning { get; private set; }
        /// <summary>流程成功进入后通知上一个流程类型与当前流程类型。</summary>
        public event Action<Type, Type> ProcedureChanged;

        internal bool IsInsideTransition => _transitionScope.Value is { } transition &&
                                            ReferenceEquals(transition, _transitionFinished);

        #region 公共方法

        /// <summary>为当前宿主登记由项目创建的流程实例。</summary>
        /// <param name="procedure">按实例的具体类型登记的流程对象。</param>
        public void Register(ProcedureBase procedure)
        {
            EnsureAvailable();
            if (procedure == null) throw new ArgumentNullException(nameof(procedure));
            Type type = procedure.GetType();
            EnsureUnregistered(type);
            _procedures.Add(type, procedure);
        }

        /// <summary>登记当前宿主的流程工厂。首次进入时执行，后续切换复用同一实例。</summary>
        /// <typeparam name="TProcedure">由工厂创建的具体流程类型。</typeparam>
        /// <param name="factory">构造该流程实例的工厂。</param>
        public void Register<TProcedure>(Func<TProcedure> factory) where TProcedure : ProcedureBase
        {
            EnsureAvailable();
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            Type type = typeof(TProcedure);
            EnsureUnregistered(type);
            _factories.Add(type, factory);
        }

        /// <summary>为启动管线指定已登记的首流程，由宿主就绪后启动。</summary>
        /// <typeparam name="TProcedure">装配器选定的首流程类型。</typeparam>
        public void ConfigureStartup<TProcedure>() where TProcedure : ProcedureBase
        {
            EnsureAvailable();
            if (_startupProcedureType != null || _context != null)
                throw new InvalidOperationException("首流程只能在流程启动前配置一次。");

            Type type = typeof(TProcedure);
            if (!_procedures.ContainsKey(type) && !_factories.ContainsKey(type))
                throw new InvalidOperationException($"请先登记首流程「{type.FullName}」。");
            _startupProcedureType = type;
        }

        /// <summary>手动启动已登记的流程。</summary>
        /// <typeparam name="TProcedure">首个流程的类型。</typeparam>
        /// <param name="context">本次运行的共享上下文，留空时创建基础上下文。</param>
        /// <param name="cancellationToken">本次启动的取消信号。</param>
        /// <returns>流程完成进入和后续自动跳转时结束的任务。</returns>
        public Task StartAsync<TProcedure>(ProcedureContext context = null, CancellationToken cancellationToken = default)
            where TProcedure : ProcedureBase => StartAsync(typeof(TProcedure), context, cancellationToken);

        /// <summary>手动启动指定类型且已登记的流程。</summary>
        /// <param name="procedureType">首个流程的类型。</param>
        /// <param name="context">本次运行的共享上下文，留空时创建基础上下文。</param>
        /// <param name="cancellationToken">本次启动的取消信号。</param>
        /// <returns>流程完成进入和后续自动跳转时结束的任务。</returns>
        public Task StartAsync(Type procedureType, ProcedureContext context = null, CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            if (_context != null) throw new InvalidOperationException("流程已启动，请使用 ChangeAsync。");
            cancellationToken.ThrowIfCancellationRequested();
            ProcedureBase first = Resolve(procedureType);
            _context = context ?? new ProcedureContext();
            return TransitionAsync(first, cancellationToken);
        }

        /// <summary>切换到另一条已登记的流程。</summary>
        /// <typeparam name="TProcedure">目标流程的类型。</typeparam>
        /// <param name="cancellationToken">本次切换的取消信号。</param>
        /// <returns>流程完成进入和后续自动跳转时结束的任务。</returns>
        public Task ChangeAsync<TProcedure>(CancellationToken cancellationToken = default)
            where TProcedure : ProcedureBase => ChangeAsync(typeof(TProcedure), cancellationToken);

        /// <summary>切换到指定类型且已登记的流程。</summary>
        /// <param name="procedureType">目标流程的类型。</param>
        /// <param name="cancellationToken">本次切换的取消信号。</param>
        /// <returns>流程完成进入和后续自动跳转时结束的任务。</returns>
        public Task ChangeAsync(Type procedureType, CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            if (IsTransitioning) throw new InvalidOperationException("流程正在切换。自动跳转请在进入流程时返回 Next<T>()。");
            if (_context == null) throw new InvalidOperationException("请先启动流程。");
            cancellationToken.ThrowIfCancellationRequested();
            return TransitionAsync(Resolve(procedureType), cancellationToken);
        }

        internal Task StartConfiguredAsync(CancellationToken token) => _startupProcedureType == null
            ? Task.CompletedTask : StartAsync(_startupProcedureType, cancellationToken: token);

        #endregion

        #region 生命周期

        void IFrameUpdate.Tick(FrameTime frameTime)
        {
            if (!IsTransitioning) _active?.Update(frameTime.ScaledDeltaTime);
        }

        protected override async Task OnShutdownAsync()
        {
            Task transition = _transitionFinished?.Task;
            try { _shutdown.Cancel(); }
            catch (Exception exception) { Log.Error("[Procedure] 关闭流程时取消失败。", exception); }
            if (transition != null) await transition;
            await ClearAsync();
            _procedures.Clear();
            _factories.Clear();
            _startupProcedureType = null;
            ProcedureChanged = null;
            _shutdown.Dispose();
        }

        #endregion

        #region 私有逻辑

        private async Task TransitionAsync(ProcedureBase next, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            CancellationToken token = linked.Token;
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> outerTransition = _transitionScope.Value;
            // 按本次切换标记异步回调，避免回调等待正在等待自己的宿主停止。
            _transitionScope.Value = finished;
            _transitionFinished = finished;
            IsTransitioning = true;
            try
            {
                for (int count = 0; count < 32; count++)
                {
                    token.ThrowIfCancellationRequested();
                    ProcedureBase previous = _active;
                    _active = null;
                    if (previous != null) await previous.ExitAsync(token);
                    token.ThrowIfCancellationRequested();

                    _active = next;
                    Type following = await next.EnterAsync(_context, token);
                    token.ThrowIfCancellationRequested();
                    NotifyChanged(previous?.GetType(), next.GetType());
                    token.ThrowIfCancellationRequested();
                    if (following == null) return;
                    next = Resolve(following);
                }
                throw new InvalidOperationException("流程连续自动跳转超过 32 次，请检查配置。");
            }
            catch
            {
                await ClearAsync();
                throw;
            }
            finally
            {
                _transitionScope.Value = outerTransition;
                IsTransitioning = false;
                _transitionFinished = null;
                finished.TrySetResult(true);
            }
        }

        private async Task ClearAsync()
        {
            ProcedureBase active = _active;
            _active = null;
            try
            {
                if (active != null) await active.ExitAsync(CancellationToken.None);
            }
            catch (Exception exception) { Log.Error("[Procedure] 退出流程失败。", exception); }
            finally
            {
                try { _context?.Dispose(); }
                catch (Exception exception) { Log.Error("[Procedure] 释放流程上下文失败。", exception); }
                finally { _context = null; }
            }
        }

        private ProcedureBase Resolve(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (_procedures.TryGetValue(type, out ProcedureBase procedure)) return procedure;
            if (!_factories.TryGetValue(type, out Func<ProcedureBase> factory))
                throw new InvalidOperationException($"流程「{type.FullName}」尚未登记，请先在当前宿主注册流程实例或工厂。");

            _isCreatingProcedure = true;
            try
            {
                procedure = factory();
                if (procedure == null || procedure.GetType() != type)
                    throw new InvalidOperationException($"流程「{type.FullName}」的工厂必须返回该类型的实例。");

                _procedures.Add(type, procedure);
                _factories.Remove(type);
                return procedure;
            }
            finally { _isCreatingProcedure = false; }
        }

        private void EnsureUnregistered(Type type)
        {
            if (_procedures.ContainsKey(type) || _factories.ContainsKey(type))
                throw new InvalidOperationException($"当前宿主已登记流程「{type.FullName}」。");
        }

        private void NotifyChanged(Type previous, Type current)
        {
            try { ProcedureChanged?.Invoke(previous, current); }
            catch (Exception exception) { Log.Error("[Procedure] 切换通知失败。", exception); }
        }

        private void EnsureAvailable()
        {
            if (_shutdown.IsCancellationRequested) throw new ObjectDisposedException(nameof(ProcedureModule));
            if (_isCreatingProcedure) throw new InvalidOperationException("流程工厂执行期间不能修改流程装配或切换流程。");
        }

        #endregion
    }
}
