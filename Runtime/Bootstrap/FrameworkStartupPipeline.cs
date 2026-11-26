using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>按启动配置装配系统，协调本地预热、常驻资源、首流程和可选热更。</summary>
    internal sealed class FrameworkStartupPipeline
    {
        private readonly TaskCompletionSource<bool> _assetsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _hotUpdateReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private FrameworkStartupConfig _config;
        private ModuleManager _modules;
        private Component _owner;
        private CancellationToken _token;
        private bool _canPresentRetry;
        private bool _isReady;
        private bool _isStopping;
        private ProcedureModule _procedures;
        private bool _hasHotUpdateResidentAssets;
        private HotUpdateStartup _hotUpdate;
        private HotUpdateStatus _stoppedHotUpdateStatus;
        private Task<HotUpdateStatus> _hotUpdateAttempt;
        private Task _assetAttempt;
        private Task _procedureAttempt;
        private StartupRetryView _assetRetryView;
        private StartupRetryView _hotUpdateRetryView;

        internal Task AssetsReady => _assetsReady.Task;
        internal bool IsInsideHotUpdateEntry => _hotUpdate?.IsInsideEntry == true;
        internal HotUpdateStatus HotUpdateStatus => _hotUpdate?.Status ?? _stoppedHotUpdateStatus;
        internal Exception AssetError { get; private set; }

        internal Task HotUpdateReady
        {
            get
            {
                if (IsInsideHotUpdateEntry)
                    throw new InvalidOperationException("热更入口不能等待由自身完成的就绪任务。");
                return _hotUpdateReady.Task;
            }
        }

        #region 生命周期

        internal void Bind(FrameworkStartupConfig config, ModuleManager modules, Component owner,
            bool canPresentRetry, CancellationToken token)
        {
            _config = config;
            _modules = modules;
            _owner = owner;
            _canPresentRetry = canPresentRetry;
            _token = token;
            if (config.HotUpdate == null && config.HotUpdateChannel == null) return;
            _hotUpdate = new HotUpdateStartup(config, config.HotUpdate, modules);
            _hasHotUpdateResidentAssets = config.HasHotUpdateResidentAssets();
        }

        internal async Task PrepareAsync(Action<ModuleComposition> configure)
        {
            _token.ThrowIfCancellationRequested();
            var composition = new ModuleComposition();
            _config.AppendModules(composition, _hotUpdate);
            configure?.Invoke(composition);
            _config.AppendSceneModule(composition);
            _token.ThrowIfCancellationRequested();
            await _modules.StartAsync(composition, _token);

            _token.ThrowIfCancellationRequested();
            await _config.WarmLocalPackagesAsync(_modules.Get<AssetModule>(), _token);
            _token.ThrowIfCancellationRequested();
            _procedures = _modules.Get<ProcedureModule>();
        }

        internal void Start()
        {
            if (_isStopping) return;
            _isReady = true;
            if (_hotUpdate == null) _hotUpdateReady.TrySetResult(true);
            // 首流程可以同时等待常驻资源或热更就绪，由宿主统一收尾。
            if (_procedures != null) _procedureAttempt = StartProcedureAsync();
            if (_config.ResidentAssets.Count == 0) _assetsReady.TrySetResult(true);
            else _ = RetryAssetsAsync();
            if (_hotUpdate != null) _ = RetryHotUpdateAsync();
        }

        internal void CancelReadiness(Exception startupFailure)
        {
            _isStopping = true;
            _hotUpdate?.CloseGate();
            // 先解除首流程的就绪等待，停止时才能继续收尾。
            _assetsReady.TrySetCanceled();
            if (startupFailure == null) _hotUpdateReady.TrySetCanceled();
            else if (_hotUpdateReady.TrySetException(startupFailure)) _ = _hotUpdateReady.Task.Exception;
        }

        internal void HideRetryViews()
        {
            _assetRetryView?.Hide();
            _hotUpdateRetryView?.Hide();
        }

        internal Task WaitForAttemptsAsync() => Task.WhenAll(_hotUpdateAttempt ?? Task.CompletedTask,
            _assetAttempt ?? Task.CompletedTask, _procedureAttempt ?? Task.CompletedTask);

        internal void CloseHotUpdateScope() => _hotUpdate?.CloseReleaseScope();

        internal async Task ShutdownAsync()
        {
            try
            {
                if (_hotUpdate != null) await _hotUpdate.ShutdownAsync();
            }
            finally
            {
                // 保留退出后的诊断结果，释放本次启动持有的模块和界面引用。
                _stoppedHotUpdateStatus = _hotUpdate?.Status;
                _hotUpdate = null;
                _config = null;
                _modules = null;
                _owner = null;
                _procedures = null;
                _token = default;
                _assetAttempt = null;
                _procedureAttempt = null;
                _hotUpdateAttempt = null;
                _assetRetryView?.Release();
                _hotUpdateRetryView?.Release();
                _assetRetryView = null;
                _hotUpdateRetryView = null;
            }
        }

        #endregion

        #region 公共方法

        internal Task RetryAssetsAsync()
        {
            if (!_isReady || _isStopping) return _assetsReady.Task;
            if (_hasHotUpdateResidentAssets && !_hotUpdateReady.Task.IsCompletedSuccessfully)
                return _assetsReady.Task;
            if (_assetsReady.Task.IsCompleted) return Task.CompletedTask;
            if (_assetAttempt != null && !_assetAttempt.IsCompleted) return _assetAttempt;
            return _assetAttempt = LoadStartupAssetsAsync();
        }

        internal Task<HotUpdateStatus> RetryHotUpdateAsync()
        {
            if (IsInsideHotUpdateEntry)
                throw new InvalidOperationException("热更入口不能等待所属发行重试。");
            HotUpdateStatus status = HotUpdateStatus;
            if (status == null || _isStopping || !_isReady) return Task.FromResult(status);
            if (_hotUpdateAttempt != null && !_hotUpdateAttempt.IsCompleted) return _hotUpdateAttempt;
            if (status.IsReady || status.IsRestartRequired) return Task.FromResult(status);
            var completion = new TaskCompletionSource<HotUpdateStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            _hotUpdateAttempt = completion.Task;
            _ = CompleteHotUpdateAsync(completion);
            return _hotUpdateAttempt;
        }

        #endregion

        #region 私有逻辑

        private async Task StartProcedureAsync()
        {
            try { await _procedures.StartConfiguredAsync(_token); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Log.Error("[Startup] 启动流程执行失败。", exception); }
        }

        private async Task LoadStartupAssetsAsync()
        {
            try
            {
                using var scope = _modules.EnterScope();
                AssetError = null;
                _assetRetryView?.Hide();
                int frame = Time.frameCount;
                do
                {
                    await Task.Yield();
                    _token.ThrowIfCancellationRequested();
                }
                while (Application.isPlaying && Time.frameCount == frame);

                AssetModule assets = _modules.Require<AssetModule>();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_token);
                var loads = new List<Task>(_config.ResidentAssets.Count);
                double deadline = Time.realtimeSinceStartupAsDouble + _config.StartupAssetTimeout;
                foreach (string address in _config.ResidentAssets)
                    loads.Add(assets.PinAsync(address, attempt.Token));
                Task batch = Task.WhenAll(loads);
                while (!batch.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
                    await Task.Yield();
                if (!batch.IsCompleted)
                {
                    attempt.Cancel();
                    try { await batch; }
                    catch (Exception) when (attempt.IsCancellationRequested) { }
                    _token.ThrowIfCancellationRequested();
                    throw new TimeoutException("启动常驻资源加载超时。");
                }

                await batch;
                _token.ThrowIfCancellationRequested();
                _assetsReady.TrySetResult(true);
            }
            catch (Exception exception)
            {
                if (_token.IsCancellationRequested) return;
                AssetError = exception;
                Log.Error("[Startup] 启动常驻资源加载失败。", exception);
                if (_canPresentRetry && _owner != null)
                {
                    _assetRetryView ??= new StartupRetryView(_owner.transform, _config, () => _ = RetryAssetsAsync());
                    _assetRetryView.Show();
                }
            }
        }

        private async Task CompleteHotUpdateAsync(TaskCompletionSource<HotUpdateStatus> completion)
        {
            try
            {
                _hotUpdateRetryView?.Hide();
                HotUpdateStatus status = await _hotUpdate.RunAsync(_token);
                if (_isStopping) return;
                if (status.IsReady)
                {
                    _hotUpdateReady.TrySetResult(true);
                    if (_hasHotUpdateResidentAssets) _ = RetryAssetsAsync();
                    return;
                }
                if (status.Error == null) return;

                string summary = $"发行 {status.ReleaseId ?? "未选定"} · {status.Stage}\n{status.Item}\n{status.Error.Message}";
                var exception = new InvalidOperationException(summary, status.Error);
                Debug.LogException(exception, _owner);
                if (status.IsRestartRequired && _hotUpdateReady.TrySetException(exception))
                    _ = _hotUpdateReady.Task.Exception;
                if (_canPresentRetry && _owner != null)
                {
                    _hotUpdateRetryView ??= new StartupRetryView(_owner.transform, _config,
                        () => _ = RetryHotUpdateAsync(), "热更启动失败");
                    _hotUpdateRetryView.Show(status.IsRestartRequired ? summary + "\n请退出进程后重试。" : summary, status.CanRetry);
                }
            }
            catch (Exception exception) { Debug.LogException(exception, _owner); }
            finally { completion.TrySetResult(HotUpdateStatus); }
        }

        #endregion
    }
}
