using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    /// <summary>将 Unity 场景生命周期接到框架启动与关闭。</summary>
    public sealed class FrameworkLauncher : MonoBehaviour
    {
        [SerializeField] private FrameworkStartupConfig _startupConfig;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _startupAssetsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new();
        private ModuleManager _modules;
        private Task _assetAttempt;
        private StartupRetryView _retryView;

        public static FrameworkLauncher Instance { get; private set; }
        public FrameworkStartupConfig StartupConfig => _startupConfig;
        public FrameworkStartupConfig ActiveConfig { get; private set; }
        public Task Ready => _ready.Task;
        public Task StartupAssetsReady => _startupAssetsReady.Task;
        public Exception StartupAssetError { get; private set; }

        private void Awake()
        {
            if (!ReferenceEquals(Instance, null) && !ReferenceEquals(Instance, this))
            {
                _ready.TrySetCanceled();
                _startupAssetsReady.TrySetCanceled();
                _lifetime.Dispose();
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            RedirectLogToUnity();
            _ = BootAsync();
        }

        private async Task BootAsync()
        {
            CancellationToken token = _lifetime.Token;
            try
            {
                FrameworkStartupConfig config = ActiveConfig = FrameworkStartupConfig.Resolve(_startupConfig);
                Runtime.Configure(config.RuntimeMode);
                if (config.ShowDebugWindow && RuntimeDebugWindow.Instance == null)
                    gameObject.AddComponent<RuntimeDebugWindow>();
                Type startupProcedureType = config.StartupProcedureType;
                _modules = new ModuleManager();
                await _modules.StartAsync(modules =>
                {
                    config.RegisterAssets(modules);
                    if (startupProcedureType != null) modules.Register<ProcedureModule>(order: 100);
                    ModuleRegistry.RegisterAll(modules);
                });
                token.ThrowIfCancellationRequested();
                await config.WarmLocalPackagesAsync(token);
                token.ThrowIfCancellationRequested();
                config.Apply();
                if (!_ready.TrySetResult(true)) return;
                if (startupProcedureType != null)
                    _ = StartProcedureSafelyAsync(startupProcedureType);
                if (config.ResidentAssets.Count == 0) _startupAssetsReady.TrySetResult(true);
                else _ = RetryStartupAssetsAsync();
            }
            catch (OperationCanceledException) when (_ready.Task.IsCanceled) { }
            catch (Exception exception)
            {
                if (_ready.TrySetException(exception)) _ = _ready.Task.Exception;
                Log.Error("[Startup] 框架初始化失败。", exception);
                Stop();
            }
        }

        /// <summary>开始或重试启动常驻资源。StartupAssetsReady 在加载成功后完成。</summary>
        public Task RetryStartupAssetsAsync()
        {
            if (!_ready.Task.IsCompletedSuccessfully || _lifetime.IsCancellationRequested)
                return _startupAssetsReady.Task;
            if (_startupAssetsReady.Task.IsCompleted) return Task.CompletedTask;
            if (_assetAttempt != null && !_assetAttempt.IsCompleted) return _assetAttempt;
            return _assetAttempt = LoadStartupAssetsAsync(ActiveConfig, _lifetime.Token);
        }

        private async Task LoadStartupAssetsAsync(FrameworkStartupConfig config, CancellationToken token)
        {
            try
            {
                StartupAssetError = null;
                _retryView?.Hide();
                int frame = Time.frameCount;
                do
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                }
                while (Application.isPlaying && Time.frameCount == frame);

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                var loads = new List<Task>(config.ResidentAssets.Count);
                double deadline = Time.realtimeSinceStartupAsDouble + config.StartupAssetTimeout;
                foreach (string address in config.ResidentAssets)
                    loads.Add(AssetModule.Instance.PinAsync(address, attempt.Token));
                Task batch = Task.WhenAll(loads);
                while (!batch.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
                    await Task.Yield();
                if (!batch.IsCompleted)
                {
                    attempt.Cancel();
                    try { await batch; }
                    catch (Exception) when (attempt.IsCancellationRequested) { }
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("启动常驻资源加载超时。");
                }
                await batch;
                token.ThrowIfCancellationRequested();
                _startupAssetsReady.TrySetResult(true);
            }
            catch (Exception exception)
            {
                if (token.IsCancellationRequested) return;
                StartupAssetError = exception;
                Log.Error("[Startup] 启动常驻资源加载失败。", exception);
                _retryView ??= new StartupRetryView(transform, config, () => _ = RetryStartupAssetsAsync());
                _retryView.Show();
            }
        }

        private void Update()
        {
            if (_ready.Task.IsCompletedSuccessfully) _modules?.Tick(Time.deltaTime);
        }
        private void OnApplicationQuit() => Stop();
        private void OnDestroy() => Stop();

        private void Stop()
        {
            if (!ReferenceEquals(Instance, this) || _lifetime.IsCancellationRequested) return;
            _ready.TrySetCanceled();
            _startupAssetsReady.TrySetCanceled();
            _lifetime.Cancel();
            _retryView?.Hide();
            ModuleManager modules = _modules;
            _modules = null;
            _ = StopModulesAsync(modules);
        }

        private async Task StopModulesAsync(ModuleManager modules)
        {
            try
            {
                if (modules != null) await modules.StopAsync();
            }
            finally
            {
                ActiveConfig = null;
                if (ReferenceEquals(Instance, this))
                {
                    Instance = null;
                    Runtime.Configure(RuntimeMode.Editor);
                }
                _lifetime.Dispose();
            }
        }

        private static void RedirectLogToUnity()
        {
            Log.Handler = (level, msg) =>
            {
                switch (level)
                {
                    case LogLevel.Warn: Debug.LogWarning(msg); break;
                    case LogLevel.Error: Debug.LogError(msg); break;
                    default: Debug.Log(msg); break;
                }
            };
            Log.ExceptionHandler = (_, exception) => Debug.LogException(exception);
        }

        private static async Task StartProcedureSafelyAsync(Type startupProcedureType)
        {
            try
            {
                await ProcedureModule.Instance.StartAsync(startupProcedureType);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Log.Error("[Startup] 启动流程执行失败。", exception);
            }
        }
    }
}
