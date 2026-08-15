using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public sealed class ResourceProcedure : ProcedureBase
    {
        protected override async Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            LoadingModule.Instance?.SetMessage(
                $"正在准备资源（{EasyRuntime.Mode}）");
            AssetModule assets = AssetModule.Instance;
            bool requiresAssets = Context.Preset.RequiresAssets;
            if (assets == null)
            {
                if (requiresAssets)
                    throw new InvalidOperationException("AssetModule is required by the startup preset.");
                return typeof(HotUpdateProcedure);
            }
            if (!assets.HasLoader)
            {
                if (requiresAssets)
                    throw new InvalidOperationException(
                        "The startup preset contains asset tasks, but AssetModule has no loader. " +
                        "Configure one through ModuleRegistry.AddRegistrar before startup.");
                return typeof(HotUpdateProcedure);
            }
            if (!assets.IsReady && !await assets.InitializeAsync())
                throw new InvalidOperationException("AssetModule initialization failed.");
            cancellationToken.ThrowIfCancellationRequested();
            LoadingModule.Instance?.Report(0.2f);
            LoadingModule.Instance?.SetMessage("正在检查更新");
            return typeof(HotUpdateProcedure);
        }
    }
}
