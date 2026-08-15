using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public sealed class ProcedureContext : IDisposable
    {
        private IDisposable _loadingScope;

        public EasyFrameworkStartupPreset Preset { get; }

        public ProcedureContext(EasyFrameworkStartupPreset preset)
        {
            Preset = preset ?? throw new ArgumentNullException(nameof(preset));
        }

        internal void SetLoadingScope(IDisposable loadingScope)
        {
            _loadingScope?.Dispose();
            _loadingScope = loadingScope;
        }

        internal async Task CompleteLoadingAsync(
            LoadingModule loading,
            TimeSpan minimumDuration,
            CancellationToken cancellationToken)
        {
            if (loading != null && _loadingScope != null)
            {
                float minimumSeconds = (float)Math.Max(0d, minimumDuration.TotalSeconds);
                float animationStartTime = loading.VisibleDuration;
                float animationDuration = Math.Max(0f, minimumSeconds - animationStartTime);
                float initialProgress = loading.Progress;

                // Always cross a Unity continuation point so the active LoadingView can render.
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                    float animationElapsed = loading.VisibleDuration - animationStartTime;
                    float progress = animationDuration <= 0f
                        ? 1f
                        : Math.Min(1f, animationElapsed / animationDuration);
                    loading.Report(initialProgress + (1f - initialProgress) * progress);
                } while (loading.VisibleDuration < minimumSeconds || loading.VisibleFrameCount < 2);

                loading.Report(1f);
            }

            EndLoading();
        }

        internal void EndLoading()
        {
            _loadingScope?.Dispose();
            _loadingScope = null;
        }

        public void Dispose() => EndLoading();
    }
}