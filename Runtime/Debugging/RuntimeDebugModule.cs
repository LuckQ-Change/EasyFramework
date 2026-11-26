using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;
using UnityEngine.Profiling;

namespace EasyFramework.Debugging
{
    /// <summary>采集运行时诊断数据，并把当前页面的只读快照交给绘制层。</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class RuntimeDebugModule : MonoBehaviour
    {
        private const int LOG_CAPACITY = 500;
        private const int LOG_MESSAGE_LIMIT = 4096;
        private const int LOG_STACK_LIMIT = 16384;
        private const int DETAIL_LIMIT = 64;
        private const float FRAME_WINDOW_SECONDS = 0.5f;
        private const float PAGE_INTERVAL_SECONDS = 0.2f;
        private static readonly RuntimeDebugSnapshot _emptyLogs = new(RuntimeDebugPage.Logs);
        private static readonly RuntimeDebugSnapshot _assetsUnavailable = new(RuntimeDebugPage.Assets);
        private static readonly RuntimeDebugSnapshot _uiUnavailable = new(RuntimeDebugPage.UI);
        private static readonly RuntimeDebugSnapshot _startupUnavailable = new(RuntimeDebugPage.Startup);

        private readonly object _logGate = new();
        private readonly RuntimeDebugLogEntry[] _logBuffer = new RuntimeDebugLogEntry[LOG_CAPACITY];
        private readonly List<AssetLeaseUsage> _leaseBuffer = new(DETAIL_LIMIT);
        private readonly List<UIStateController> _stateBuffer = new(DETAIL_LIMIT);
        private FrameworkLauncher _launcher;
        private FrameworkStartupConfig _startupConfig;
        private Application.LogCallback _logCallback;
        private int _logGeneration;
        private int _logWriteIndex;
        private int _logCount;
        private long _logSequence;
        private long _logRevision;
        private long _publishedLogRevision;
        private RuntimeDebugPage _visiblePage;
        private Coroutine _pageSampler;
        private int _frameCount;
        private float _frameSeconds;
        private float _peakFrameSeconds;
        private float _averageFrameMilliseconds;
        private float _peakFrameMilliseconds;

        /// <summary>供折叠状态的徽标读取的平均帧率。</summary>
        public float FramesPerSecond { get; private set; }

        /// <summary>最近一次发布的页面快照，可在主线程安全读取。</summary>
        public RuntimeDebugSnapshot Snapshot { get; private set; } = RuntimeDebugSnapshot.Empty;

        #region 生命周期

        private void OnEnable()
        {
            int generation;
            lock (_logGate) generation = ++_logGeneration;

            _logCallback = (message, stackTrace, type) => OnLog(generation, message, stackTrace, type);
            Application.logMessageReceivedThreaded += _logCallback;
            if (_visiblePage != RuntimeDebugPage.None) _pageSampler = StartCoroutine(SampleVisiblePage());
        }

        private void OnDisable()
        {
            if (_logCallback != null)
            {
                Application.logMessageReceivedThreaded -= _logCallback;
                _logCallback = null;
            }

            lock (_logGate)
            {
                // 已取到旧回调的线程可能还在运行，代数使它无法写入下次启用后的缓冲。
                _logGeneration++;
                ClearLogBuffer();
            }

            StopPageSampler();
            _visiblePage = RuntimeDebugPage.None;
            Snapshot = RuntimeDebugSnapshot.Empty;
            _leaseBuffer.Clear();
            _stateBuffer.Clear();
            _frameCount = 0;
            _frameSeconds = 0f;
            _peakFrameSeconds = 0f;
            _averageFrameMilliseconds = 0f;
            _peakFrameMilliseconds = 0f;
            FramesPerSecond = 0f;
        }

        private void Update()
        {
            // 徽标仅使用帧间隔做常数开销统计，折叠时不会执行模块探针。
            float seconds = Time.unscaledDeltaTime;
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return;

            _frameSeconds += seconds;
            _frameCount++;
            _peakFrameSeconds = Mathf.Max(_peakFrameSeconds, seconds);
            if (_frameSeconds < FRAME_WINDOW_SECONDS) return;

            FramesPerSecond = _frameCount / _frameSeconds;
            _averageFrameMilliseconds = _frameSeconds * 1000f / _frameCount;
            _peakFrameMilliseconds = _peakFrameSeconds * 1000f;
            _frameCount = 0;
            _frameSeconds = 0f;
            _peakFrameSeconds = 0f;
        }

        #endregion

        #region 公共方法

        internal void Initialize(FrameworkLauncher launcher, FrameworkStartupConfig config)
        {
            _launcher = launcher;
            _startupConfig = config;
        }

        /// <summary>在主线程选择当前可见的诊断页。收起面板时停止页面采样。</summary>
        /// <param name="page">当前页面，None 表示面板收起。</param>
        public void SetVisiblePage(RuntimeDebugPage page)
        {
            if (_visiblePage == page) return;
            _visiblePage = page;
            Snapshot = RuntimeDebugSnapshot.Empty;

            if (page == RuntimeDebugPage.None) StopPageSampler();
            else if (isActiveAndEnabled && _pageSampler == null) _pageSampler = StartCoroutine(SampleVisiblePage());
        }

        /// <summary>在主线程清除保留的日志，并更新可见日志页。</summary>
        public void ClearLogs()
        {
            lock (_logGate)
            {
                ClearLogBuffer();
                if (_visiblePage != RuntimeDebugPage.Logs) return;
                _publishedLogRevision = _logRevision;
            }
            Snapshot = _emptyLogs;
        }

        #endregion

        #region 私有逻辑

        private void OnLog(int generation, string message, string stackTrace, LogType type)
        {
            // Unity 可并发派发该回调。只操作受锁保护的内存，不访问 Unity 对象。
            lock (_logGate)
            {
                if (generation != _logGeneration) return;
                _logBuffer[_logWriteIndex] = new RuntimeDebugLogEntry(++_logSequence, DateTime.UtcNow, type,
                    LimitLogText(message, LOG_MESSAGE_LIMIT), LimitLogText(stackTrace, LOG_STACK_LIMIT));
                _logWriteIndex = (_logWriteIndex + 1) % LOG_CAPACITY;
                if (_logCount < LOG_CAPACITY) _logCount++;
                _logRevision++;
            }
        }

        private static string LimitLogText(string value, int limit)
        {
            if (value == null) return string.Empty;
            if (value.Length <= limit) return value;

            // 为截断标记留一个字符，并保持 UTF-16 代理对完整。
            int end = limit - 1;
            if (char.IsHighSurrogate(value[end - 1]) && char.IsLowSurrogate(value[end])) end--;
            return value.Substring(0, end) + "…";
        }

        private void ClearLogBuffer()
        {
            Array.Clear(_logBuffer, 0, _logBuffer.Length);
            _logWriteIndex = 0;
            _logCount = 0;
            _logRevision++;
        }

        private IEnumerator SampleVisiblePage()
        {
            // 第一次采样也延后到协程，OnGUI 切换页签时只更新采样目标。
            var interval = new WaitForSecondsRealtime(PAGE_INTERVAL_SECONDS);
            while (true)
            {
                yield return interval;
                switch (_visiblePage)
                {
                    case RuntimeDebugPage.Logs: PublishLogs(); break;
                    case RuntimeDebugPage.Performance: PublishPerformance(); break;
                    case RuntimeDebugPage.Assets: PublishAssets(); break;
                    case RuntimeDebugPage.UI: PublishUI(); break;
                    case RuntimeDebugPage.Startup: PublishStartup(); break;
                }
            }
        }

        private void StopPageSampler()
        {
            if (_pageSampler == null) return;
            StopCoroutine(_pageSampler);
            _pageSampler = null;
        }

        private void PublishLogs()
        {
            RuntimeDebugLogEntry[] entries;
            lock (_logGate)
            {
                if (Snapshot.Page == RuntimeDebugPage.Logs && _publishedLogRevision == _logRevision) return;
                _publishedLogRevision = _logRevision;
                entries = _logCount == 0 ? Array.Empty<RuntimeDebugLogEntry>() : new RuntimeDebugLogEntry[_logCount];
                int first = (_logWriteIndex - _logCount + LOG_CAPACITY) % LOG_CAPACITY;
                int firstPart = Math.Min(_logCount, LOG_CAPACITY - first);
                Array.Copy(_logBuffer, first, entries, 0, firstPart);
                Array.Copy(_logBuffer, 0, entries, firstPart, _logCount - firstPart);
            }
            Snapshot = entries.Length == 0
                ? _emptyLogs
                : new RuntimeDebugSnapshot(RuntimeDebugPage.Logs, logs: Array.AsReadOnly(entries));
        }

        private void PublishPerformance()
        {
            var performance = new RuntimeDebugPerformanceSnapshot(FramesPerSecond, _averageFrameMilliseconds,
                _peakFrameMilliseconds, GC.GetTotalMemory(false), Profiler.GetTotalAllocatedMemoryLong(),
                Profiler.GetTotalReservedMemoryLong(), Application.targetFrameRate, QualitySettings.vSyncCount);
            Snapshot = new RuntimeDebugSnapshot(RuntimeDebugPage.Performance, performance: performance);
        }

        private void PublishAssets()
        {
            AssetModule assets = _launcher != null ? _launcher.Modules?.Get<AssetModule>() : null;
            if (assets == null)
            {
                Snapshot = _assetsUnavailable;
                return;
            }

            AssetCacheSnapshot totals = assets.GetSnapshot();
            assets.CopyActiveLeaseUsage(_leaseBuffer, DETAIL_LIMIT);
            Snapshot = new RuntimeDebugSnapshot(RuntimeDebugPage.Assets, assets: totals,
                assetLeases: Array.AsReadOnly(_leaseBuffer.ToArray()));
            _leaseBuffer.Clear();
        }

        private void PublishUI()
        {
            ModuleManager hostModules = _launcher != null ? _launcher.Modules : null;
            if (hostModules == null)
            {
                Snapshot = _uiUnavailable;
                return;
            }

            using var scope = hostModules.EnterScope();
            // Current 只读取现有管理器，诊断不会触发 UI 宿主的创建。
            UIManager manager = UIManager.Current;
            if (manager == null)
            {
                Snapshot = _uiUnavailable;
                return;
            }

            UIRuntimeHost host = manager.RuntimeHost;
            if (host != null) host.GetComponentsInChildren(true, _stateBuffer);
            int total = _stateBuffer.Count;
            int count = Math.Min(total, DETAIL_LIMIT);
            var states = new RuntimeDebugUIState[count];
            for (int i = 0; i < count; i++)
            {
                UIStateController state = _stateBuffer[i];
                if (state != null)
                    states[i] = new RuntimeDebugUIState(state.name, state.GetInstanceID(), state.InitialState,
                        state.SelectedState, state.isActiveAndEnabled);
            }
            _stateBuffer.Clear();

            IReadOnlyList<UIPageResourceUsage> pages = manager.GetResourceSnapshot();
            if (pages is List<UIPageResourceUsage> pageList) pages = pageList.AsReadOnly();
            Snapshot = new RuntimeDebugSnapshot(RuntimeDebugPage.UI, pages: pages, uiOpenCount: manager.OpenCount,
                uiStates: Array.AsReadOnly(states), uiStateCount: total);
        }

        private void PublishStartup()
        {
            FrameworkLauncher launcher = _launcher;
            if (launcher == null)
            {
                Snapshot = _startupUnavailable;
                return;
            }

            // 回滚会清除活动配置。保留装配时的配置引用以定位失败的宿主。
            FrameworkStartupConfig config = launcher.ActiveConfig;
            if (config == null) config = _startupConfig;
            string configName = null;
            RuntimeMode? mode = null;
            string baselineId = null;
            string channelId = null;
            if (config != null)
            {
                configName = config.name;
                mode = config.RuntimeMode;
                baselineId = config.PlayerBaselineId;
                if (config.HotUpdateChannel != null) channelId = config.HotUpdateChannel.ChannelId;
            }

            RuntimeDebugStartupSnapshot? previous = Snapshot.Startup;
            Task ready = launcher.Ready;
            TaskStatus hostState = ready.Status;
            string hostError = hostState == TaskStatus.Faulted
                ? previous?.HostError ?? ready.Exception?.GetBaseException().Message : null;
            var startup = new RuntimeDebugStartupSnapshot(configName, mode, baselineId, channelId,
                launcher.Modules != null, hostState, hostError, launcher.StartupAssetsReady.Status,
                launcher.StartupAssetError?.Message, launcher.HotUpdateStatus);
            if (previous is RuntimeDebugStartupSnapshot last && last.Matches(startup)) return;
            Snapshot = new RuntimeDebugSnapshot(RuntimeDebugPage.Startup, startup: startup);
        }

        #endregion
    }
}
