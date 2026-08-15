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
            Type type = typeof(TProcedure);
            if (_procedures.TryGetValue(type, out ProcedureBase existing))
                return (TProcedure)existing;
            var procedure = new TProcedure();
            _procedures.Add(type, procedure);
            return procedure;
        }

        public Task StartAsync<TProcedure>(
            ProcedureContext context,
            CancellationToken cancellationToken = default(CancellationToken))
            where TProcedure : ProcedureBase
        {
            if (_context != null)
                throw new InvalidOperationException("The procedure flow has already started.");
            _context = context ?? throw new ArgumentNullException(nameof(context));
            return ChangeAsync(typeof(TProcedure), cancellationToken);
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
                throw new InvalidOperationException("Start the procedure module with a ProcedureContext first.");
            if (!_procedures.ContainsKey(procedureType))
                throw new InvalidOperationException($"Procedure '{procedureType.FullName}' is not registered.");

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
                            throw new InvalidOperationException("Procedure chain exceeded 32 automatic transitions.");
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

        public Task EnterGameAsync(CancellationToken cancellationToken = default(CancellationToken)) =>
            ChangeAsync<GameProcedure>(cancellationToken);

        public Task ExitGameAsync(CancellationToken cancellationToken = default(CancellationToken)) =>
            ChangeAsync<ExitProcedure>(cancellationToken);

        protected override void OnInit()
        {
            _shutdown = new CancellationTokenSource();
            Register<StartupProcedure>();
            Register<ResourceProcedure>();
            Register<HotUpdateProcedure>();
            Register<PreloadProcedure>();
            Register<LoginProcedure>();
            Register<GameProcedure>();
            Register<ExitProcedure>();
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

            ProcedureBase nextProcedure = _procedures[procedureType];
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
                Log.Error($"[Procedure] shutdown exit failed: {exception}");
            }
        }
    }
}
