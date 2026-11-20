using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>管理当前流程和上下文，同一时间只允许执行一次切换。</summary>
    public sealed class ProcedureModule : Module<ProcedureModule>, IFrameUpdate
    {
        private readonly Dictionary<Type, ProcedureBase> _procedures = new();
        private readonly CancellationTokenSource _shutdown = new();
        private ProcedureContext _context;
        private ProcedureBase _active;
        private TaskCompletionSource<bool> _transitionFinished;

        public ProcedureBase ActiveProcedure => _active;
        public Type ActiveProcedureType => _active?.GetType();
        public bool IsTransitioning { get; private set; }
        public event Action<Type, Type> ProcedureChanged;

        public void Register(ProcedureBase procedure)
        {
            EnsureAvailable();
            if (procedure == null) throw new ArgumentNullException(nameof(procedure));
            _procedures.Add(procedure.GetType(), procedure);
        }

        public Task StartAsync<TProcedure>(ProcedureContext context = null, CancellationToken cancellationToken = default)
            where TProcedure : ProcedureBase => StartAsync(typeof(TProcedure), context, cancellationToken);

        public Task StartAsync(Type procedureType, ProcedureContext context = null, CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            if (_context != null) throw new InvalidOperationException("流程已启动，请使用 ChangeAsync。");
            cancellationToken.ThrowIfCancellationRequested();
            ProcedureBase first = Resolve(procedureType);
            _context = context ?? new ProcedureContext();
            return TransitionAsync(first, cancellationToken);
        }

        public Task ChangeAsync<TProcedure>(CancellationToken cancellationToken = default)
            where TProcedure : ProcedureBase => ChangeAsync(typeof(TProcedure), cancellationToken);

        public Task ChangeAsync(Type procedureType, CancellationToken cancellationToken = default)
        {
            EnsureAvailable();
            if (IsTransitioning) throw new InvalidOperationException("流程正在切换。自动跳转请在进入流程时返回 Next<T>()。");
            if (_context == null) throw new InvalidOperationException("请先启动流程。");
            cancellationToken.ThrowIfCancellationRequested();
            return TransitionAsync(Resolve(procedureType), cancellationToken);
        }

        void IFrameUpdate.Tick(float deltaTime)
        {
            if (!IsTransitioning) _active?.Update(deltaTime);
        }

        protected override async Task OnShutdownAsync()
        {
            Task transition = _transitionFinished?.Task;
            try { _shutdown.Cancel(); }
            catch (Exception exception) { Log.Error("[Procedure] 关闭流程时取消失败。", exception); }
            if (transition != null) await transition;
            await ClearAsync();
            _procedures.Clear();
            ProcedureChanged = null;
            _shutdown.Dispose();
        }

        private async Task TransitionAsync(ProcedureBase next, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            CancellationToken token = linked.Token;
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            if (!typeof(ProcedureBase).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException($"流程“{type.FullName}”必须是具有公开无参构造函数的 ProcedureBase，或预先注册实例。");

            procedure = (ProcedureBase)Activator.CreateInstance(type);
            _procedures.Add(type, procedure);
            return procedure;
        }

        private void NotifyChanged(Type previous, Type current)
        {
            try { ProcedureChanged?.Invoke(previous, current); }
            catch (Exception exception) { Log.Error("[Procedure] 切换通知失败。", exception); }
        }

        private void EnsureAvailable()
        {
            if (_shutdown.IsCancellationRequested) throw new ObjectDisposedException(nameof(ProcedureModule));
        }
    }
}
