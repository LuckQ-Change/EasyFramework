using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public sealed class HotUpdateProcedure : ProcedureBase
    {
        protected override async Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            EasyFrameworkStartupPreset preset = Context.Preset;
            if (!preset.ShouldRunHotUpdate)
            {
                LoadingModule.Instance?.Report(0.55f);
                LoadingModule.Instance?.SetMessage("正在预加载");
                return typeof(PreloadProcedure);
            }

            AssetModule assets = AssetModule.Instance;
            HotfixModule hotfix = HotfixModule.Instance;
            if (assets == null || hotfix == null)
                throw new InvalidOperationException("Hot update requires AssetModule and HotfixModule.");

            var metadata = new List<byte[]>();
            IReadOnlyList<string> metadataLocations = preset.AotMetadataLocations;
            for (int i = 0; i < metadataLocations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssetLease<byte[]> lease = await assets.AcquireAsync<byte[]>(metadataLocations[i]);
                try
                {
                    if (lease?.Asset == null)
                        throw new InvalidOperationException($"AOT metadata was not found: {metadataLocations[i]}");
                    metadata.Add(lease.Asset);
                }
                finally { lease?.Dispose(); }
            }
            hotfix.LoadAotMetadata(metadata);

            if (!string.IsNullOrWhiteSpace(preset.HotfixAssemblyLocation))
            {
                AssetLease<byte[]> lease = await assets.AcquireAsync<byte[]>(preset.HotfixAssemblyLocation);
                Assembly assembly;
                try
                {
                    assembly = hotfix.LoadHotfix(lease?.Asset);
                    if (assembly == null)
                        throw new InvalidOperationException(
                            $"Hotfix assembly failed to load: {preset.HotfixAssemblyLocation}");
                }
                finally { lease?.Dispose(); }

                if (!string.IsNullOrWhiteSpace(preset.HotfixEntryType) &&
                    !string.IsNullOrWhiteSpace(preset.HotfixEntryMethod))
                {
                    hotfix.InvokeEntry(assembly, preset.HotfixEntryType, preset.HotfixEntryMethod);
                }
            }
            LoadingModule.Instance?.Report(0.55f);
            LoadingModule.Instance?.SetMessage("正在预加载");
            return typeof(PreloadProcedure);
        }
    }
}
