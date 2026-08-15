using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;

namespace EasyFramework
{
    public sealed class PreloadProcedure : ProcedureBase
    {
        private static readonly TimeSpan MinimumLoadingDuration = TimeSpan.FromSeconds(2d);

        protected override async Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            EasyFrameworkStartupPreset preset = Context.Preset;
            int total = preset.PreloadUILocations.Count;
            int completed = 0;
            IReadOnlyList<string> uiLocations = preset.PreloadUILocations;
            for (int i = 0; i < uiLocations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await EasyUIManager.Instance.PreloadAsync(uiLocations[i]))
                    throw new InvalidOperationException($"UI preload failed: {uiLocations[i]}");
                completed++;
                LoadingModule.Instance?.Report(0.55f + 0.35f * completed / total);
            }
            await Context.CompleteLoadingAsync(
                LoadingModule.Instance,
                MinimumLoadingDuration,
                cancellationToken);
            return typeof(LoginProcedure);
        }
    }
}
