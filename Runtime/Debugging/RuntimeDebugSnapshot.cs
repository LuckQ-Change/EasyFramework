using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework.Debugging
{
    /// <summary>诊断模块当前要采样的页面。</summary>
    public enum RuntimeDebugPage
    {
        None,
        Logs,
        Performance,
        Assets,
        UI,
        Startup,
    }

    /// <summary>从 Unity 日志入口收集的一条日志及其堆栈。</summary>
    public readonly struct RuntimeDebugLogEntry
    {
        /// <summary>本次采集会话内递增的序号。</summary>
        public long Sequence { get; }

        /// <summary>收到日志时的 UTC 时间。</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Unity 日志等级。</summary>
        public LogType Type { get; }

        /// <summary>日志正文。</summary>
        public string Message { get; }

        /// <summary>Unity 提供的堆栈文本。</summary>
        public string StackTrace { get; }

        internal RuntimeDebugLogEntry(long sequence, DateTime timestampUtc, LogType type, string message, string stackTrace)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            Type = type;
            Message = message;
            StackTrace = stackTrace;
        }
    }

    /// <summary>性能页最近一次采样的帧率、帧耗时和内存数据。</summary>
    public readonly struct RuntimeDebugPerformanceSnapshot
    {
        /// <summary>最近测量时段内的平均每秒帧数。</summary>
        public float FramesPerSecond { get; }

        /// <summary>最近测量时段内的平均帧间隔，单位为毫秒。</summary>
        public float AverageFrameMilliseconds { get; }

        /// <summary>最近测量时段内最长的帧间隔，单位为毫秒。</summary>
        public float PeakFrameMilliseconds { get; }

        /// <summary>托管对象估算占用的内存字节数。</summary>
        public long ManagedUsedBytes { get; }

        /// <summary>Unity 分配器已分配的字节数。</summary>
        public long AllocatedBytes { get; }

        /// <summary>Unity 分配器预留的字节数。</summary>
        public long ReservedBytes { get; }

        /// <summary>应用配置的目标帧率。</summary>
        public int TargetFrameRate { get; }

        /// <summary>质量设置中的垂直同步间隔。</summary>
        public int VSyncCount { get; }

        internal RuntimeDebugPerformanceSnapshot(float framesPerSecond, float averageFrameMilliseconds,
            float peakFrameMilliseconds, long managedUsedBytes, long allocatedBytes, long reservedBytes,
            int targetFrameRate, int vSyncCount)
        {
            FramesPerSecond = framesPerSecond;
            AverageFrameMilliseconds = averageFrameMilliseconds;
            PeakFrameMilliseconds = peakFrameMilliseconds;
            ManagedUsedBytes = managedUsedBytes;
            AllocatedBytes = allocatedBytes;
            ReservedBytes = reservedBytes;
            TargetFrameRate = targetFrameRate;
            VSyncCount = vSyncCount;
        }
    }

    /// <summary>只包含文本和数值的 UIState 控制器状态。</summary>
    public readonly struct RuntimeDebugUIState
    {
        /// <summary>控制器所在节点的名称。</summary>
        public string ControllerName { get; }

        /// <summary>控制器在本次运行中的实例标识。</summary>
        public int InstanceId { get; }

        /// <summary>控制器配置的初始状态。</summary>
        public string InitialState { get; }

        /// <summary>采样时选中的运行状态。</summary>
        public string SelectedState { get; }

        /// <summary>控制器及其所在的节点层级是否启用。</summary>
        public bool IsActive { get; }

        internal RuntimeDebugUIState(string controllerName, int instanceId, string initialState, string selectedState,
            bool isActive)
        {
            ControllerName = controllerName;
            InstanceId = instanceId;
            InitialState = initialState;
            SelectedState = selectedState;
            IsActive = isActive;
        }
    }

    /// <summary>启动页采样到的本地宿主、常驻资源和热更阶段。</summary>
    public readonly struct RuntimeDebugStartupSnapshot
    {
        /// <summary>启动时使用的配置名称。</summary>
        public string ConfigurationName { get; }

        /// <summary>配置中的运行模式。</summary>
        public RuntimeMode? Mode { get; }

        /// <summary>当前 Player 配置的基线 ID。</summary>
        public string PlayerBaselineId { get; }

        /// <summary>配置中的发行渠道 ID。</summary>
        public string ChannelId { get; }

        /// <summary>采样时是否还持有本地模块宿主。</summary>
        public bool HasActiveHost { get; }

        /// <summary>本地模块和资源包就绪任务的状态。</summary>
        public TaskStatus HostState { get; }

        /// <summary>本地启动失败时的错误。</summary>
        public string HostError { get; }

        /// <summary>启动常驻资源就绪任务的状态。</summary>
        public TaskStatus StartupAssetsState { get; }

        /// <summary>最近一次启动常驻资源加载错误。</summary>
        public string StartupAssetError { get; }

        /// <summary>热更协调器已发布的当前状态。</summary>
        public HotUpdateStatus HotUpdate { get; }

        internal RuntimeDebugStartupSnapshot(string configurationName, RuntimeMode? mode, string playerBaselineId,
            string channelId, bool hasActiveHost, TaskStatus hostState, string hostError,
            TaskStatus startupAssetsState, string startupAssetError, HotUpdateStatus hotUpdate)
        {
            ConfigurationName = configurationName;
            Mode = mode;
            PlayerBaselineId = playerBaselineId;
            ChannelId = channelId;
            HasActiveHost = hasActiveHost;
            HostState = hostState;
            HostError = hostError;
            StartupAssetsState = startupAssetsState;
            StartupAssetError = startupAssetError;
            HotUpdate = hotUpdate;
        }

        internal bool Matches(RuntimeDebugStartupSnapshot other) =>
            ConfigurationName == other.ConfigurationName && Mode == other.Mode &&
            PlayerBaselineId == other.PlayerBaselineId && ChannelId == other.ChannelId &&
            HasActiveHost == other.HasActiveHost && HostState == other.HostState && HostError == other.HostError &&
            StartupAssetsState == other.StartupAssetsState && StartupAssetError == other.StartupAssetError &&
            ReferenceEquals(HotUpdate, other.HotUpdate);
    }

    /// <summary>供 OnGUI 读取的已发布诊断数据，每次发布只包含当前页。</summary>
    public sealed class RuntimeDebugSnapshot
    {
        internal static readonly RuntimeDebugSnapshot Empty = new(RuntimeDebugPage.None);

        /// <summary>快照中的数据所属页面。</summary>
        public RuntimeDebugPage Page { get; }

        /// <summary>日志从旧到新排列。</summary>
        public IReadOnlyList<RuntimeDebugLogEntry> Logs { get; }

        /// <summary>性能页的帧统计及内存数据。</summary>
        public RuntimeDebugPerformanceSnapshot Performance { get; }

        /// <summary>资源页的全局资源租约和缓存数据，未安装资源模块时为空。</summary>
        public AssetCacheSnapshot? Assets { get; }

        /// <summary>持有活跃租约的资源明细，最多包含 64 项。</summary>
        public IReadOnlyList<AssetLeaseUsage> AssetLeases { get; }

        /// <summary>UI 页的页面可见状态和每页租约。</summary>
        public IReadOnlyList<UIPageResourceUsage> Pages { get; }

        /// <summary>UI 宿主中采样到的状态控制器明细，最多包含 64 项。</summary>
        public IReadOnlyList<RuntimeDebugUIState> UIStates { get; }

        /// <summary>采样时是否存在 UI 管理器。</summary>
        public bool HasUIManager { get; }

        /// <summary>采样时 UI 管理器中的托管页面总数。</summary>
        public int UIOpenCount { get; }

        /// <summary>采样时 UI 宿主中的状态控制器总数。</summary>
        public int UIStateCount { get; }

        /// <summary>启动页的快照，未捕获启动器时为空。</summary>
        public RuntimeDebugStartupSnapshot? Startup { get; }

        internal RuntimeDebugSnapshot(RuntimeDebugPage page, IReadOnlyList<RuntimeDebugLogEntry> logs = null,
            RuntimeDebugPerformanceSnapshot performance = default, AssetCacheSnapshot? assets = null,
            IReadOnlyList<AssetLeaseUsage> assetLeases = null, IReadOnlyList<UIPageResourceUsage> pages = null,
            int uiOpenCount = 0, IReadOnlyList<RuntimeDebugUIState> uiStates = null, int uiStateCount = 0,
            RuntimeDebugStartupSnapshot? startup = null)
        {
            Page = page;
            Logs = logs ?? Array.Empty<RuntimeDebugLogEntry>();
            Performance = performance;
            Assets = assets;
            AssetLeases = assetLeases ?? Array.Empty<AssetLeaseUsage>();
            Pages = pages ?? Array.Empty<UIPageResourceUsage>();
            UIStates = uiStates ?? Array.Empty<RuntimeDebugUIState>();
            HasUIManager = pages != null;
            UIOpenCount = uiOpenCount;
            UIStateCount = uiStateCount;
            Startup = startup;
        }
    }
}
