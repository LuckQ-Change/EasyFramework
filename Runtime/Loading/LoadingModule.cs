using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace EasyFramework
{
    public readonly struct LoadingState
    {
        public bool IsLoading { get; }
        public float Progress { get; }
        public string Message { get; }

        public LoadingState(bool isLoading, float progress, string message)
        {
            IsLoading = isLoading;
            Progress = progress;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>不依赖具体 UI 实现的全局加载状态，可供启动和场景切换共用。</summary>
    public sealed class LoadingModule : ModuleSingleton<LoadingModule>
    {
        private sealed class LoadingScope : IDisposable
        {
            private LoadingModule _owner;
            public LoadingScope(LoadingModule owner) => _owner = owner;
            public void Dispose()
            {
                LoadingModule owner = Interlocked.Exchange(ref _owner, null);
                owner?.End();
            }
        }

        private sealed class DirectProgress : IProgress<float>
        {
            private readonly LoadingModule _owner;
            public DirectProgress(LoadingModule owner) => _owner = owner;
            public void Report(float value) => _owner.Report(value);
        }

        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private int _scopeCount;

        public bool IsLoading { get; private set; }
        public float Progress { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public float VisibleDuration { get; private set; }
        public int VisibleFrameCount { get; private set; }
        public LoadingState State => new LoadingState(IsLoading, Progress, Message);

        public event Action<LoadingState> Changed;

        public IDisposable Begin(string message = "加载中")
        {
            bool startsNewSession = _scopeCount == 0;
            _scopeCount++;
            if (startsNewSession)
            {
                Progress = 0f;
                VisibleDuration = 0f;
                VisibleFrameCount = 0;
            }
            IsLoading = true;
            Message = message ?? string.Empty;
            Notify();
            return new LoadingScope(this);
        }

        public void Report(float progress)
        {
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
            Notify();
        }

        public void SetMessage(string message)
        {
            Message = message ?? string.Empty;
            Notify();
        }

        public async Task RunAsync(
            string message,
            Func<IProgress<float>, Task> operation,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                using (Begin(message))
                {
                    await operation(new DirectProgress(this));
                    Report(1f);
                }
            }
            finally { _operationGate.Release(); }
        }

        public Task LoadSceneAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            string message = "正在切换场景",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("Scene name is required.", nameof(sceneName));
            cancellationToken.ThrowIfCancellationRequested();
            return RunAsync(message, async progress =>
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null)
                    throw new InvalidOperationException($"Unity could not start loading scene '{sceneName}'.");
                while (!operation.isDone)
                {
                    progress.Report(operation.progress / 0.9f);
                    await Task.Yield();
                }
                progress.Report(1f);
            }, CancellationToken.None);
        }

        protected override void OnShutdown()
        {
            _scopeCount = 0;
            IsLoading = false;
            Progress = 0f;
            VisibleDuration = 0f;
            VisibleFrameCount = 0;
            Message = string.Empty;
            Notify();
            Changed = null;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (!IsLoading) return;
            VisibleFrameCount++;
            float visibleDelta = UnityEngine.Time.unscaledDeltaTime;
            if (visibleDelta > 0f) VisibleDuration += visibleDelta;
        }

        private void End()
        {
            if (_scopeCount <= 0) return;
            _scopeCount--;
            if (_scopeCount > 0) return;
            Progress = 1f;
            IsLoading = false;
            Notify();
        }

        private void Notify() => Changed?.Invoke(State);
    }
}
