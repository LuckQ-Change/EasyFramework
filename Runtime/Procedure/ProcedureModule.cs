using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public sealed class ProcedureModule : ModuleSingleton<ProcedureModule>
    {
        private readonly Dictionary<Type, ProcedureBase> _procedures =
            new Dictionary<Type, ProcedureBase>();
        private readonly SemaphoreSlim _transitionGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _shutdown;
        private ProcedureContext _context;
        private ProcedureBase _active;

        public ProcedureBase ActiveProcedure => _active;
        public Type ActiveProcedureType => _active?.GetType();
        public bool IsTransitioning { get; private set; }

        public event Action<Type, Type> ProcedureChanged;

        public TProcedure Register<TProcedure>() where TProcedure : ProcedureBase, new()
        {
            return (TProcedure)Register((ProcedureBase)new TProcedure());
        }

        public TProcedure Register<TProcedure>(TProcedure procedure) where TProcedure : ProcedureBase
        {
            return (TProcedure)Register((ProcedureBase)procedure);
        }

        public ProcedureBase Register(Type procedureType)
        {
            if (procedureType == null) throw new ArgumentNullException(nameof(procedureType));
            if (_procedures.TryGetValue(procedureType, out ProcedureBase existing))
                return existing;

            ValidateProcedureType(procedureType);
            var procedure = Activator.CreateInstance(procedureType) as ProcedureBase;
            if (procedure == null)
                throw new InvalidOperationException($"无法创建流程“{procedureType.FullName}”。");
            _procedures.Add(procedureType, procedure);
            return procedure;
        }

        public ProcedureBase Register(ProcedureBase procedure)
        {
            if (procedure == null) throw new ArgumentNullException(nameof(procedure));
            Type type = procedure.GetType();
            if (_procedures.TryGetValue(type, out ProcedureBase existing))
                return existing;
            _procedures.Add(type, procedure);
            return procedure;
        }

        public Task StartAsync<TProcedure>(
            ProcedureContext context,
            CancellationToken cancellationToken = default(CancellationToken))
            where TProcedure : ProcedureBase
        {
            return StartAsync(typeof(TProcedure), context, cancellationToken);
        }

        public Task StartAsync(
            Type procedureType,
            ProcedureContext context,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_context != null)
                throw new InvalidOperationException("流程已经启动，不能重复启动。");
            if (procedureType == null) throw new ArgumentNullException(nameof(procedureType));
            if (context == null) throw new ArgumentNullException(nameof(context));
            Register(procedureType);
            _context = context;
            return ChangeAsync(procedureType, cancellationToken);
        }

        public Task ChangeAsync<TProcedure>(
            CancellationToken cancellationToken = default(CancellationToken))
            where TProcedure : ProcedureBase => ChangeAsync(typeof(TProcedure), cancellationToken);

        public async Task ChangeAsync(
            Type procedureType,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (procedureType == null) throw new ArgumentNullException(nameof(procedureType));
            if (_context == null)
                throw new InvalidOperationException("请先使用 ProcedureContext 启动流程模块。");
            Register(procedureType);

            CancellationToken shutdownToken = _shutdown?.Token ?? CancellationToken.None;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken, shutdownToken))
            {
                await _transitionGate.WaitAsync(linked.Token);
                IsTransitioning = true;
                try
                {
                    Type next = procedureType;
                    int automaticTransitionCount = 0;
                    while (next != null)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (++automaticTransitionCount > 32)
                            throw new InvalidOperationException("流程链连续自动跳转超过 32 次，请检查是否存在死循环。");
                        next = await EnterNextAsync(next, linked.Token);
                    }
                }
                catch
                {
                    _context?.Dispose();
                    _context = null;
                    throw;
                }
                finally
                {
                    IsTransitioning = false;
                    _transitionGate.Release();
                }
            }
        }

        protected override void OnInit()
        {
            _shutdown = new CancellationTokenSource();
        }

        protected override void OnUpdate(float deltaTime)
        {
            _active?.Update(deltaTime);
        }

        protected override void OnShutdown()
        {
            _shutdown?.Cancel();
            ProcedureBase active = _active;
            _active = null;
            if (active != null && !IsTransitioning) _ = ExitAfterShutdownAsync(active);
            _context?.Dispose();
            _context = null;
            _procedures.Clear();
            ProcedureChanged = null;
            _shutdown?.Dispose();
            _shutdown = null;
        }

        private async Task<Type> EnterNextAsync(Type procedureType, CancellationToken cancellationToken)
        {
            Type previousType = _active?.GetType();
            if (_active != null)
            {
                await _active.ExitAsync(cancellationToken);
                _active = null;
            }

            ProcedureBase nextProcedure = Register(procedureType);
            _active = nextProcedure;
            try
            {
                Type automaticNext = await nextProcedure.EnterAsync(this, _context, cancellationToken);
                ProcedureChanged?.Invoke(previousType, procedureType);
                Log.Info($"[Procedure] {procedureType.Name}");
                return automaticNext;
            }
            catch
            {
                _active = null;
                try { await nextProcedure.ExitAsync(CancellationToken.None); }
                catch { }
                throw;
            }
        }

        private static async Task ExitAfterShutdownAsync(ProcedureBase procedure)
        {
            try { await procedure.ExitAsync(CancellationToken.None); }
            catch (Exception exception)
            {
                Log.Error("[Procedure] 关闭流程时退出失败。", exception);
            }
        }

        private static void ValidateProcedureType(Type procedureType)
        {
            if (procedureType == null) throw new ArgumentNullException(nameof(procedureType));
            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
                throw new ArgumentException(
                    $"类型“{procedureType.FullName}”必须继承 ProcedureBase。",
                    nameof(procedureType));
            if (procedureType.IsAbstract)
                throw new ArgumentException(
                    $"流程类型“{procedureType.FullName}”不能是抽象类。",
                    nameof(procedureType));
            if (procedureType.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException(
                    $"流程类型“{procedureType.FullName}”必须提供公开的无参构造函数。",
                    nameof(procedureType));
        }
    }
}
