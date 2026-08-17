using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public abstract class ProcedureBase
    {
        public ProcedureContext Context { get; private set; }
        public ProcedureModule Module { get; private set; }

        internal async Task<Type> EnterAsync(
            ProcedureModule module,
            ProcedureContext context,
            CancellationToken cancellationToken)
        {
            Module = module;
            Context = context;
            return await OnEnterAsync(cancellationToken);
        }

        internal async Task ExitAsync(CancellationToken cancellationToken)
        {
            try { await OnExitAsync(cancellationToken); }
            finally
            {
                Context = null;
                Module = null;
            }
        }

        internal void Update(float deltaTime) => OnUpdate(deltaTime);

        protected abstract Task<Type> OnEnterAsync(CancellationToken cancellationToken);
        protected virtual Task OnExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected virtual void OnUpdate(float deltaTime) { }

        /// <summary>获取项目自定义上下文；类型不匹配时直接报告清晰错误。</summary>
        protected TContext RequireContext<TContext>() where TContext : ProcedureContext
        {
            if (Context is TContext context) return context;
            string actual = Context == null ? "null" : Context.GetType().FullName;
            throw new InvalidOperationException(
                $"流程“{GetType().FullName}”需要上下文“{typeof(TContext).FullName}”，" +
                $"当前上下文为“{actual}”。");
        }

        protected static Task<Type> Next<TProcedure>() where TProcedure : ProcedureBase =>
            Task.FromResult(typeof(TProcedure));

        protected static Task<Type> Stay() => Task.FromResult<Type>(null);
    }
}
