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
    public sealed class LoadingModule : Module<LoadingModule>, IProgress<float>
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

        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private int _scopeCount;
        private bool _shuttingDown;

        public bool IsLoading => _scopeCount != 0;
        public float Progress { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public LoadingState State => new LoadingState(IsLoading, Progress, Message);

        public event Action<LoadingState> Changed;

        public IDisposable Begin(string message = "加载中")
        {
            if (_shuttingDown) throw new InvalidOperationException("LoadingModule is shutting down.");
            if (_scopeCount++ == 0) Progress = 0f;
            Message = message ?? string.Empty;
            Notify();
            return new LoadingScope(this);
        }

        public void Report(float progress)
        {
            if (_shuttingDown) return;
            if (float.IsNaN(progress) || float.IsInfinity(progress))
                throw new ArgumentOutOfRangeException(nameof(progress));
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
            Notify();
        }

        public void SetMessage(string message)
        {
            if (_shuttingDown) return;
            Message = message ?? string.Empty;
            Notify();
        }

        public async Task RunAsync(
            string message,
            Func<IProgress<float>, Task> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                using var scope = Begin(message);
                cancellationToken.ThrowIfCancellationRequested();
                await operation(this);
                Report(1f);
            }
            finally { _operationGate.Release(); }
        }

        public Task LoadSceneAsync(
            string sceneName,
            LoadSceneMode mode = LoadSceneMode.Single,
            string message = "正在切换场景",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("Scene name is required.", nameof(sceneName));
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
            }, cancellationToken);
        }

        protected override void OnShutdown()
        {
            _shuttingDown = true;
            _scopeCount = 0;
            Progress = 0f;
            Message = string.Empty;
            Notify();
            Changed = null;
        }

        private void End()
        {
            if (_scopeCount == 0 || --_scopeCount != 0) return;
            Progress = 1f;
            Notify();
        }

        private void Notify()
        {
            Action<LoadingState> handlers = Changed;
            if (handlers == null) return;
            LoadingState state = State;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<LoadingState>)handler)(state); }
                catch (Exception exception) { Log.Error("[Loading] state listener failed.", exception); }
            }
        }
    }
}
