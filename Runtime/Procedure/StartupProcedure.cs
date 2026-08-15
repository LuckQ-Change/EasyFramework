using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public sealed class StartupProcedure : ProcedureBase
    {
        protected override Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            LoadingModule loading = LoadingModule.Instance;
            if (loading != null)
            {
                Context.SetLoadingScope(loading.Begin("正在启动"));
            }
            Context.Preset.Apply();
            loading?.Report(0.05f);
            return Next<ResourceProcedure>();
        }
    }
}
