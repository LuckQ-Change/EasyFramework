using System;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>选择宿主是否提供全局画布和默认模块作用域。</summary>
    public enum FrameworkHostMode
    {
        [InspectorName("默认宿主")]
        Default = 0,
        [InspectorName("隔离宿主")]
        Isolated = 1,
    }

    /// <summary>把 Unity 启停和帧循环连接到当前对象持有的模块宿主。</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class FrameworkLauncher : MonoBehaviour
    {
        [SerializeField, InspectorName("启动配置")] private FrameworkStartupConfig _startupPreset;
        [SerializeField] private FrameworkHostMode _hostMode = FrameworkHostMode.Default;
        [Tooltip("关闭后，通过 StartAsync 显式提供配置和装配。")]
        [SerializeField] private bool _startAutomatically = true;

        private static FrameworkLauncher _defaultLauncher;

        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _bootFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly FrameworkStartupPipeline _startup = new();
        private FrameworkHostMode _activeHostMode;
        private bool _hasStarted;
        private bool _isStopping;
        private Exception _startupFailure;
        private UIRuntimeHost _ownedUI;

        /// <summary>默认宿主解析配置后、模块启动前通知可选功能装配。</summary>
        public static event Action<FrameworkLauncher, FrameworkStartupConfig> Starting;

        /// <summary>当前调用可访问的默认宿主。隔离宿主通过自身引用访问。</summary>
        public static FrameworkLauncher Instance
        {
            get
            {
                ModuleManager current = ModuleManager.Current;
                if (current == null || ReferenceEquals(current, _defaultLauncher?.Modules)) return _defaultLauncher;
                return null;
            }
        }

        /// <summary>组件保存的启动配置，独立于一次启动的显式参数。</summary>
        public FrameworkStartupConfig StartupConfig => _startupPreset;

        /// <summary>当前会话使用的宿主模式。</summary>
        public FrameworkHostMode HostMode => _hasStarted ? _activeHostMode : _hostMode;

        /// <summary>本次启动实际使用的配置。</summary>
        public FrameworkStartupConfig ActiveConfig { get; private set; }

        /// <summary>当前宿主独享的模块管理器。模块通过其 Get 或 Require 查询。</summary>
        public ModuleManager Modules { get; private set; }

        /// <summary>模块和启动阶段的本地资源包就绪任务。</summary>
        public Task Ready => _ready.Task;

        /// <summary>启动常驻资源全部准备完成的任务。</summary>
        public Task StartupAssetsReady => _startup.AssetsReady;

        /// <summary>等待热更入口成功。可重试失败时继续等待，需重启的失败会返回异常。</summary>
        public Task HotUpdateReady => _startup.HotUpdateReady;

        /// <summary>当前热更阶段和错误，未配置热更时为空。</summary>
        public HotUpdateStatus HotUpdateStatus => _startup.HotUpdateStatus;

        /// <summary>最近一次常驻资源加载失败的原因。</summary>
        public Exception StartupAssetError => _startup.AssetError;

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStartupCallbacks()
        {
            _defaultLauncher = null;
            Starting = null;
        }

        private void Awake() => TryStartAutomatically();
        private void OnEnable() => TryStartAutomatically();

        private void TryStartAutomatically()
        {
            if (!enabled || !_startAutomatically || _hasStarted || _isStopping) return;
            if (_hostMode == FrameworkHostMode.Default && !ReferenceEquals(_defaultLauncher, null))
            {
                _ = RequestStop();
                Destroy(gameObject);
                return;
            }

            _ = BeginStartup(_startupPreset, _hostMode, null);
        }

        private void Update()
        {
            if (!_isStopping && _ready.Task.IsCompletedSuccessfully)
                Modules?.Tick(new FrameTime(Time.deltaTime, Time.unscaledDeltaTime));
        }

        private void OnApplicationQuit()
        {
            Modules?.Get<SceneModule>()?.NotifyApplicationQuit();
            _ = RequestStop();
        }
        private void OnDestroy() => _ = RequestStop();

        #endregion

        #region 公共方法

        /// <summary>在 Unity 主线程，于已启用且关闭自动启动的组件上显式装配并启动宿主。</summary>
        /// <param name="configuration">本次启动要使用的配置资产。</param>
        /// <param name="hostMode">是否提供全局画布和默认模块作用域。</param>
        /// <param name="configure">声明项目模块、工厂以及依赖的可选回调。</param>
        /// <returns>完成模块启动和本地资源包准备时结束的任务。</returns>
        public Task StartAsync(FrameworkStartupConfig configuration, FrameworkHostMode hostMode,
            Action<ModuleComposition> configure = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (!isActiveAndEnabled || _startAutomatically)
                throw new InvalidOperationException("请启用启动器并关闭自动启动，再调用 StartAsync。");
            return BeginStartup(configuration, hostMode, configure);
        }

        /// <summary>在 Unity 主线程取消当前启动或任务，并等待本宿主完成逆序停止。</summary>
        /// <returns>清理完成时结束的任务，重复调用返回同一任务。</returns>
        public Task StopAsync()
        {
            if (Modules?.IsInsideLifecycleCallback == true)
                throw new InvalidOperationException("不能在同一宿主的初始化或关闭回调中等待停止。");
            if (Modules?.Get<SceneModule>()?.IsInsideOperation == true)
                throw new InvalidOperationException("不能在场景操作的准备或进度回调中等待所属启动器停止。");
            if (Modules?.Get<ProcedureModule>()?.IsInsideTransition == true)
                throw new InvalidOperationException("请在流程切换完成后，从外部请求停止所属启动器。");
            if (_startup.IsInsideHotUpdateEntry)
                throw new InvalidOperationException("热更入口不能等待停止它所属的启动器。");
            return RequestStop();
        }

        /// <summary>开始或重试启动常驻资源加载。</summary>
        /// <returns>本次常驻加载结束的任务。启动还未就绪时返回常驻资源就绪任务。</returns>
        public Task RetryStartupAssetsAsync() => _startup.RetryAssetsAsync();

        /// <summary>在 Unity 主线程开始或重试同一热更发行。重复调用共享正在执行的尝试。</summary>
        /// <returns>本次结果。未配置热更时为空，成功或需重启时直接返回当前结果。</returns>
        public Task<HotUpdateStatus> RetryHotUpdateAsync() => _startup.RetryHotUpdateAsync();

        #endregion

        #region 私有逻辑

        private Task RequestStop()
        {
            if (_isStopping) return _stopped.Task;

            _isStopping = true;
            _startup.CancelReadiness(_startupFailure);
            // 本地流程也可能正在等待场景任务。
            Modules?.Get<SceneModule>()?.RequestStop();
            try { _lifetime.Cancel(); }
            catch (Exception exception) { Log.Error("[Startup] 取消宿主任务失败。", exception); }
            _startup.HideRetryViews();
            _ = FinishStopAsync();
            return _stopped.Task;
        }

        private Task BeginStartup(FrameworkStartupConfig configuration, FrameworkHostMode hostMode,
            Action<ModuleComposition> configure)
        {
            if (_hasStarted || _isStopping)
                throw new InvalidOperationException("每个 FrameworkLauncher 只管理一次启动会话。");

            _activeHostMode = hostMode;
            _hasStarted = true;
            _ = BootAsync(configuration, configure);
            return _ready.Task;
        }

        private async Task BootAsync(FrameworkStartupConfig config, Action<ModuleComposition> configure)
        {
            CancellationToken token = _lifetime.Token;
            try
            {
                if (config == null) throw new InvalidOperationException("请为启动器指定 Startup Config。");
                config.ValidateStartupMode();
                if (config.PageManifest != null) config.PageManifest.Validate();
                bool isDefault = HostMode == FrameworkHostMode.Default;
                if (!isDefault && HostMode != FrameworkHostMode.Isolated)
                    throw new InvalidOperationException("FrameworkLauncher 的宿主模式无效。");
                if (isDefault)
                {
                    if (!ReferenceEquals(_defaultLauncher, null))
                        throw new InvalidOperationException("项目只能启用一个默认宿主。");
                    _defaultLauncher = this;
                    DontDestroyOnLoad(gameObject);
                    RedirectLogToUnity();
                }

                ActiveConfig = config;
                var modules = new ModuleManager(useDefault: isDefault, runtimeMode: config.RuntimeMode);
                Modules = modules;
                _startup.Bind(config, modules, this, isDefault, token);
                using var scope = modules.EnterScope();
                if (isDefault) NotifyStarting(config);
                await _startup.PrepareAsync(configure);
                token.ThrowIfCancellationRequested();
                if (isDefault) config.Apply(out _ownedUI);
                token.ThrowIfCancellationRequested();

                _ready.TrySetResult(true);
                _startup.Start();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _startupFailure = exception;
                // 隔离宿主也将启动错误写入 Unity Console，无需更改全局日志处理器。
                Debug.LogException(exception, this);
                _ = RequestStop();
            }
            finally { _bootFinished.TrySetResult(true); }
        }

        private void NotifyStarting(FrameworkStartupConfig configuration)
        {
            Action<FrameworkLauncher, FrameworkStartupConfig> listeners = Starting;
            if (listeners == null) return;

            foreach (Action<FrameworkLauncher, FrameworkStartupConfig> listener in listeners.GetInvocationList())
            {
                try { listener(this, configuration); }
                catch (Exception exception) { Log.Error("[Startup] 启动扩展失败。", exception); }
            }
        }

        private async Task FinishStopAsync()
        {
            try
            {
                if (_hasStarted) await _bootFinished.Task;
                await _startup.WaitForAttemptsAsync();
            }
            catch (Exception exception) { Log.Error("[Startup] 等待宿主任务收尾失败。", exception); }

            ModuleManager modules = Modules;
            try
            {
                using var scope = modules?.EnterScope();
                try { ReleaseOwnedUI(); }
                catch (Exception exception) { Log.Error("[Startup] 释放宿主 UI 失败。", exception); }
                // 释放本次发行登记的订阅和资源时，所属宿主的模块仍可访问。
                _startup.CloseHotUpdateScope();
                try
                {
                    if (modules != null) await modules.StopAsync();
                }
                finally
                {
                    await _startup.ShutdownAsync();
                }
            }
            catch (Exception exception) { Log.Error("[Startup] 停止模块宿主失败。", exception); }
            finally
            {
                Modules = null;
                ActiveConfig = null;
                if (ReferenceEquals(_defaultLauncher, this)) _defaultLauncher = null;
                _lifetime.Dispose();

                // 启动失败和取消都在停止所占用的模块后对外完成。
                if (_startupFailure == null) _ready.TrySetCanceled();
                else if (_ready.TrySetException(_startupFailure)) _ = _ready.Task.Exception;
                _stopped.TrySetResult(true);
            }
        }

        private void ReleaseOwnedUI()
        {
            UIRuntimeHost ui = _ownedUI;
            _ownedUI = null;
            if (ui == null) return;

            // 立即归还窗口租约，避免延迟到 Destroy 后才触发。
            try { UIManager.ReleaseHost(ui); }
            finally
            {
                if (ui != null)
                {
                    ui.gameObject.SetActive(false);
                    DestroyOwnedObject(ui.gameObject);
                }
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }

        private static void RedirectLogToUnity()
        {
            Log.Handler = (level, message) =>
            {
                switch (level)
                {
                    case LogLevel.Warn: Debug.LogWarning(message); break;
                    case LogLevel.Error: Debug.LogError(message); break;
                    default: Debug.Log(message); break;
                }
            };
            Log.ExceptionHandler = (_, exception) => Debug.LogException(exception);
        }

        #endregion
    }
}
