using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EasyFramework.Debugging;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>在安全区内展示诊断快照，并在展开时选择需要采样的页面。</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class RuntimeDebugWindow : MonoBehaviour
    {
        private const int MAX_LOG_CAPACITY = 500;
        private const int PREVIEW_LENGTH = 240;
        private const float LOG_ROW_HEIGHT = 24f;
        private const float PANEL_WIDTH = 682f;
        private const float PANEL_HEIGHT = 510f;
        private const float MIN_PANEL_WIDTH = 160f;
        private const float MIN_PANEL_HEIGHT = 95f;
        private static readonly string[] _pageLabels = { "日志", "性能", "资源", "UI", "启动" };
        private static readonly string[] _logLabels = { "信息", "警告", "错误", "异常" };
        private static readonly RuntimeDebugPage[] _pages =
            { RuntimeDebugPage.Logs, RuntimeDebugPage.Performance, RuntimeDebugPage.Assets, RuntimeDebugPage.UI, RuntimeDebugPage.Startup };
        private static readonly IReadOnlyList<RuntimeDebugLogEntry> _emptyLogs = Array.Empty<RuntimeDebugLogEntry>();

        private readonly List<LogRow> _visibleLogs = new(MAX_LOG_CAPACITY);
        private readonly GUIContent _textContent = new();
        private RuntimeDebugModule _module;
        private GUI.WindowFunction _drawBadge;
        private GUI.WindowFunction _drawPanel;
        private GUIStyle _logStyle;
        private GUIStyle _detailStyle;
        private GUISkin _skin;
        private Rect _windowRect;
        private Vector2 _badgePosition = new(12f, 12f);
        private Vector2 _panelPosition = new(12f, 12f);
        private int _windowId;
        private bool _isOpen;
        private int _pageIndex;
        private int _displayedFps = int.MinValue;
        private string _fpsLabel;
        private Vector2 _pageScroll;
        private Vector2 _logScroll;
        private Vector2 _detailScroll;
        private string _search = string.Empty;
        private int _levelMask = 15;
        private bool _isFilterDirty = true;
        private bool _isFollowing = true;
        private bool _isLogScrollEndRequested;
        private bool _isLogPaused;
        private bool _hasSelectedLog;
        private long _selectedSequence;
        private string _logDetail = string.Empty;
        private IReadOnlyList<RuntimeDebugLogEntry> _displayedLogs = _emptyLogs;
        private IReadOnlyList<RuntimeDebugLogEntry> _pausedLogs = _emptyLogs;
        private RuntimeDebugSnapshot _textSnapshot;
        private string _pageText;
        private string _measuredText;
        private float _measuredWidth;
        private float _measuredHeight;

        /// <summary>缓存一次可见日志在列表中的显示内容。</summary>
        private readonly struct LogRow
        {
            internal RuntimeDebugLogEntry Entry { get; }
            internal string Label { get; }

            internal LogRow(RuntimeDebugLogEntry entry, string label)
            {
                Entry = entry;
                Label = label;
            }
        }

        #region 生命周期

        private void Awake()
        {
            _windowId = GetInstanceID();
            _drawBadge = DrawBadge;
            _drawPanel = DrawPanel;
        }

        private void OnEnable() => UpdateVisiblePage();

        private void OnDisable()
        {
            if (_module != null) _module.SetVisiblePage(RuntimeDebugPage.None);
        }

        private void OnGUI()
        {
            if (_module == null) return;

            Rect safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f)
            {
                _module.SetVisiblePage(RuntimeDebugPage.None);
                return;
            }

            // Unity 安全区以屏幕左下角计量，OnGUI 则以左上角为原点。
            float scale = Mathf.Clamp(Mathf.Min(safe.width, safe.height) / 680f, 0.7f, 2f);
            Rect bounds = new(safe.xMin / scale, (Screen.height - safe.yMax) / scale,
                safe.width / scale, safe.height / scale);
            Matrix4x4 matrix = GUI.matrix;
            int depth = GUI.depth;
            Color color = GUI.color;
            Color background = GUI.backgroundColor;
            Color content = GUI.contentColor;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                GUI.depth = -10000;
                GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
                HandleShortcut();
                if (_isOpen)
                {
                    _windowRect = Constrain(bounds, _panelPosition, PANEL_WIDTH, PANEL_HEIGHT);
                    UpdateVisiblePage();
                    Rect dragged = GUI.Window(_windowId, _windowRect, _drawPanel, "EasyFramework 诊断");
                    _panelPosition = Constrain(bounds, dragged.position, PANEL_WIDTH, PANEL_HEIGHT).position;
                }
                else
                {
                    _windowRect = Constrain(bounds, _badgePosition, 116f, 60f);
                    Rect dragged = GUI.Window(_windowId, _windowRect, _drawBadge, "诊断");
                    _badgePosition = Constrain(bounds, dragged.position, 116f, 60f).position;
                }
            }
            finally
            {
                GUI.matrix = matrix;
                GUI.depth = depth;
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
            }
        }

        #endregion

        #region 公共方法

        /// <summary>是否正在显示诊断详情。</summary>
        public bool IsOpen => _isOpen;

        /// <summary>展开诊断窗口。</summary>
        public void Open() => SetOpen(true);

        /// <summary>收起详情并停止页面采样。</summary>
        public void Close() => SetOpen(false);

        internal void Initialize(RuntimeDebugModule module)
        {
            if (_module != null && _module != module) _module.SetVisiblePage(RuntimeDebugPage.None);
            _module = module;
            UpdateVisiblePage();
        }

        #endregion

        #region 私有逻辑

        private void HandleShortcut()
        {
            Event input = Event.current;
            if (input.type != EventType.KeyDown || input.keyCode != KeyCode.F8) return;
            SetOpen(!_isOpen);
            input.Use();
        }

        private void SetOpen(bool isOpen)
        {
            if (_isOpen == isOpen) return;
            _isOpen = isOpen;
            UpdateVisiblePage();
        }

        private void UpdateVisiblePage()
        {
            if (_module == null) return;
            bool isPageVisible = _isOpen && isActiveAndEnabled &&
                                 _windowRect.width >= MIN_PANEL_WIDTH && _windowRect.height >= MIN_PANEL_HEIGHT;
            _module.SetVisiblePage(isPageVisible ? _pages[_pageIndex] : RuntimeDebugPage.None);
        }

        private static Rect Constrain(Rect bounds, Vector2 position, float width, float height)
        {
            float edge = Mathf.Min(8f, Mathf.Min(bounds.width, bounds.height) * 0.1f);
            width = Mathf.Min(width, bounds.width - edge * 2f);
            height = Mathf.Min(height, bounds.height - edge * 2f);
            return new Rect(
                Mathf.Clamp(position.x, bounds.xMin + edge, bounds.xMax - edge - width),
                Mathf.Clamp(position.y, bounds.yMin + edge, bounds.yMax - edge - height),
                width, height);
        }

        private void DrawBadge(int windowId)
        {
            int fps = Mathf.RoundToInt(_module.FramesPerSecond);
            if (_displayedFps != fps)
            {
                _displayedFps = fps;
                _fpsLabel = fps > 0 ? $"FPS {fps}" : "FPS —";
            }

            if (_windowRect.width > 12f && _windowRect.height > 29f &&
                GUI.Button(new Rect(6f, 23f, _windowRect.width - 12f, _windowRect.height - 29f), _fpsLabel)) Open();
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 22f));
        }

        private void DrawPanel(int windowId)
        {
            float width = _windowRect.width;
            float height = _windowRect.height;
            if (GUI.Button(new Rect(width - 62f, 3f, 55f, 20f), "收起"))
            {
                Close();
                return;
            }

            GUI.DragWindow(new Rect(0f, 0f, width - 66f, 22f));
            if (height < MIN_PANEL_HEIGHT || width < MIN_PANEL_WIDTH) return;
            EnsureStyles();

            int current = GUI.Toolbar(new Rect(8f, 25f, width - 16f, 27f), _pageIndex, _pageLabels);
            if (current != _pageIndex)
            {
                _pageIndex = current;
                _pageScroll = Vector2.zero;
                UpdateVisiblePage();
            }

            var area = new Rect(8f, 58f, width - 16f, height - 66f);
            RuntimeDebugSnapshot snapshot = _module.Snapshot;
            if (_pages[_pageIndex] == RuntimeDebugPage.Logs)
            {
                DrawLogs(area, snapshot.Page == RuntimeDebugPage.Logs ? snapshot.Logs : _displayedLogs);
                return;
            }

            if (!ReferenceEquals(snapshot, _textSnapshot))
            {
                _textSnapshot = snapshot;
                _pageText = BuildPageText(snapshot);
            }

            string text = snapshot.Page == _pages[_pageIndex] ? _pageText : "等待页面快照";
            DrawText(area, text, ref _pageScroll);
        }

        private void DrawLogs(Rect area, IReadOnlyList<RuntimeDebugLogEntry> liveLogs)
        {
            if (area.height < 108f || area.width < 260f)
            {
                GUI.Label(area, "扩大 Game 视图以查看日志。");
                return;
            }

            if (!DrawLogControls(area, liveLogs)) return;
            IReadOnlyList<RuntimeDebugLogEntry> source = _isLogPaused ? _pausedLogs : liveLogs;
            RefreshVisibleLogs(source);
            float listHeight = Mathf.Max(34f, (area.height - 60f) * 0.56f);
            Rect list = new(area.x, area.y + 60f, area.width, listHeight);
            DrawLogList(list);
            float detailTop = list.yMax + 5f;
            Rect detail = new(area.x, detailTop, area.width, Mathf.Max(0f, area.yMax - detailTop));
            DrawLogDetail(detail);
        }

        private bool DrawLogControls(Rect area, IReadOnlyList<RuntimeDebugLogEntry> liveLogs)
        {
            const float BUTTON_WIDTH = 52f;
            const float ROW = 23f;
            const float SEARCH_LABEL_WIDTH = 38f;
            float buttonsX = area.xMax - BUTTON_WIDTH * 3f - 8f;
            GUI.Label(new Rect(area.x, area.y, SEARCH_LABEL_WIDTH, ROW), "搜索");
            float searchX = area.x + SEARCH_LABEL_WIDTH;
            string search = GUI.TextField(new Rect(searchX, area.y, buttonsX - searchX - 4f, ROW), _search);
            if (_search != search)
            {
                _search = search;
                _isFilterDirty = true;
            }

            if (GUI.Button(new Rect(buttonsX, area.y, BUTTON_WIDTH, ROW), _isLogPaused ? "继续" : "暂停"))
            {
                _isLogPaused = !_isLogPaused;
                _pausedLogs = _isLogPaused ? liveLogs : _emptyLogs;
                _isFilterDirty = true;
            }
            if (GUI.Button(new Rect(buttonsX + BUTTON_WIDTH + 4f, area.y, BUTTON_WIDTH, ROW), _isFollowing ? "跟随中" : "跟随"))
            {
                _isFollowing = !_isFollowing;
                _isLogScrollEndRequested = _isFollowing;
            }
            if (GUI.Button(new Rect(buttonsX + (BUTTON_WIDTH + 4f) * 2f, area.y, BUTTON_WIDTH, ROW), "清空"))
            {
                _module.ClearLogs();
                _displayedLogs = _pausedLogs = _emptyLogs;
                _hasSelectedLog = false;
                _logDetail = string.Empty;
                _logScroll = _detailScroll = Vector2.zero;
                _isLogScrollEndRequested = false;
                _isFilterDirty = true;
                // 这次事件收到的是清空前的快照，下一次绘制再读取。
                return false;
            }

            float toggleWidth = area.width / _logLabels.Length;
            for (int i = 0; i < _logLabels.Length; i++)
            {
                int bit = 1 << i;
                bool isShown = (_levelMask & bit) != 0;
                bool isSelected = GUI.Toggle(new Rect(area.x + toggleWidth * i, area.y + 27f, toggleWidth, ROW),
                    isShown, _logLabels[i]);
                if (isShown == isSelected) continue;
                _levelMask ^= bit;
                _isFilterDirty = true;
            }
            return true;
        }

        private void RefreshVisibleLogs(IReadOnlyList<RuntimeDebugLogEntry> source)
        {
            if (!_isFilterDirty && ReferenceEquals(_displayedLogs, source)) return;
            _isFilterDirty = false;
            _displayedLogs = source;
            _visibleLogs.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                RuntimeDebugLogEntry entry = source[i];
                int level = LogLevel(entry.Type);
                if ((_levelMask & (1 << level)) == 0) continue;
                if (_search.Length > 0 &&
                    entry.Message.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.StackTrace.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string message = GetPreview(entry.Message);
                string label = $"{entry.TimestampUtc.ToLocalTime():HH:mm:ss.fff}  [{_logLabels[level]}]  {message}";
                _visibleLogs.Add(new LogRow(entry, label));
            }
            _isLogScrollEndRequested = _isFollowing;
        }

        private void DrawLogList(Rect area)
        {
            float contentWidth = Mathf.Max(1f, area.width - 20f);
            float contentHeight = Mathf.Max(area.height, _visibleLogs.Count * LOG_ROW_HEIGHT);
            Event input = Event.current;
            if (input.type == EventType.ScrollWheel && area.Contains(input.mousePosition))
            {
                _isFollowing = false;
                _isLogScrollEndRequested = false;
            }
            if (_isLogScrollEndRequested)
            {
                _logScroll.y = contentHeight - area.height;
                _isLogScrollEndRequested = false;
            }
            _logScroll = GUI.BeginScrollView(area, _logScroll, new Rect(0f, 0f, contentWidth, contentHeight));
            try
            {
                if (_visibleLogs.Count == 0)
                {
                    GUI.Label(new Rect(5f, 1f, contentWidth - 8f, LOG_ROW_HEIGHT), "没有符合筛选条件的日志");
                    return;
                }

                // 渲染滚动区域内的行，避免密集日志在每次 IMGUI 事件时分配控件。
                int begin = Mathf.Clamp(Mathf.FloorToInt(_logScroll.y / LOG_ROW_HEIGHT), 0, _visibleLogs.Count);
                int end = Mathf.Min(_visibleLogs.Count, begin + Mathf.CeilToInt(area.height / LOG_ROW_HEIGHT) + 1);
                for (int index = begin; index < end; index++)
                {
                    LogRow row = _visibleLogs[index];
                    bool isSelected = _hasSelectedLog && _selectedSequence == row.Entry.Sequence;
                    if (GUI.Toggle(new Rect(0f, index * LOG_ROW_HEIGHT, contentWidth, LOG_ROW_HEIGHT),
                            isSelected, row.Label, _logStyle) && !isSelected)
                    {
                        _hasSelectedLog = true;
                        _selectedSequence = row.Entry.Sequence;
                        _logDetail = string.IsNullOrEmpty(row.Entry.StackTrace)
                            ? row.Entry.Message
                            : row.Entry.Message + "\n\n" + row.Entry.StackTrace;
                        _detailScroll = Vector2.zero;
                    }
                }
            }
            finally { GUI.EndScrollView(); }
        }

        private void DrawLogDetail(Rect area)
        {
            if (area.height < 27f) return;
            if (!_hasSelectedLog)
            {
                GUI.Label(area, "选择一条日志查看消息和堆栈。");
                return;
            }

            if (GUI.Button(new Rect(area.xMax - 54f, area.y, 54f, 22f), "复制"))
                GUIUtility.systemCopyBuffer = _logDetail;
            DrawText(new Rect(area.x, area.y + 25f, area.width, area.height - 25f), _logDetail, ref _detailScroll);
        }

        private void DrawText(Rect area, string text, ref Vector2 scroll)
        {
            if (area.height <= 0f) return;
            GUI.Box(area, GUIContent.none);
            float viewWidth = Mathf.Max(1f, area.width - 21f);
            float textWidth = Mathf.Max(1f, viewWidth - 12f);
            _textContent.text = text;
            if (!ReferenceEquals(_measuredText, text) || _measuredWidth != textWidth)
            {
                _measuredText = text;
                _measuredWidth = textWidth;
                _measuredHeight = _detailStyle.CalcHeight(_textContent, textWidth);
            }
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, viewWidth, Mathf.Max(area.height, _measuredHeight + 12f)));
            try
            {
                GUI.Label(new Rect(6f, 6f, textWidth, _measuredHeight), _textContent, _detailStyle);
            }
            finally { GUI.EndScrollView(); }
        }

        private void EnsureStyles()
        {
            if (_skin == GUI.skin && _logStyle != null) return;
            _skin = GUI.skin;
            _logStyle = new GUIStyle(_skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _detailStyle = new GUIStyle(_skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
            };
            _measuredWidth = 0f;
        }

        private static int LogLevel(LogType type) => type switch
        {
            LogType.Warning => 1,
            LogType.Error or LogType.Assert => 2,
            LogType.Exception => 3,
            _ => 0,
        };

        private static string GetPreview(string message)
        {
            int limit = Mathf.Min(message.Length, PREVIEW_LENGTH);
            for (int i = 0; i < limit; i++)
            {
                if (message[i] != '\r' && message[i] != '\n') continue;
                limit = i;
                break;
            }
            return limit < message.Length ? message.Substring(0, limit) + "…" : message;
        }

        private static string BuildPageText(RuntimeDebugSnapshot snapshot)
        {
            switch (snapshot.Page)
            {
                case RuntimeDebugPage.Performance:
                    RuntimeDebugPerformanceSnapshot performance = snapshot.Performance;
                    return $"FPS    {performance.FramesPerSecond:0.0}\n" +
                           $"平均帧耗时    {performance.AverageFrameMilliseconds:0.00} ms\n" +
                           $"峰值帧耗时    {performance.PeakFrameMilliseconds:0.00} ms\n\n" +
                           $"托管内存    {FormatBytes(performance.ManagedUsedBytes)}\n" +
                           $"Unity 已分配    {FormatBytes(performance.AllocatedBytes)}\n" +
                           $"Unity 已预留    {FormatBytes(performance.ReservedBytes)}\n\n" +
                           $"目标帧率    {performance.TargetFrameRate}\n垂直同步间隔    {performance.VSyncCount}";
                case RuntimeDebugPage.Assets:
                    if (snapshot.Assets is not AssetCacheSnapshot assets) return "资源模块尚未启用。";
                    return BuildAssetsText(snapshot, assets);
                case RuntimeDebugPage.UI:
                    if (!snapshot.HasUIManager) return "UI 管理器尚未就绪。";
                    return BuildUIStateText(snapshot);
                case RuntimeDebugPage.Startup:
                    return snapshot.Startup is RuntimeDebugStartupSnapshot startup
                        ? BuildStartupText(startup) : "框架启动器不可用。";
                default:
                    return string.Empty;
            }
        }

        private static string BuildStartupText(RuntimeDebugStartupSnapshot startup)
        {
            string hostState = startup.HostState == TaskStatus.RanToCompletion && !startup.HasActiveHost
                ? "已停止" : StartupTaskLabel(startup.HostState);
            string assetState = startup.StartupAssetError == null
                ? StartupTaskLabel(startup.StartupAssetsState) : "加载失败";
            var text = new StringBuilder()
                .Append("启动配置    ").AppendLine(startup.ConfigurationName ?? "—")
                .Append("运行模式    ").AppendLine(startup.Mode?.ToString() ?? "—")
                .Append("本地宿主    ").AppendLine(hostState)
                .Append("常驻资源    ").AppendLine(assetState);
            if (!string.IsNullOrEmpty(startup.HostError))
                text.Append("启动错误    ").AppendLine(startup.HostError);
            if (!string.IsNullOrEmpty(startup.StartupAssetError))
                text.Append("资源错误    ").AppendLine(startup.StartupAssetError);

            HotUpdateStatus update = startup.HotUpdate;
            if (update == null) return text.AppendLine().Append("热更    未配置").ToString();

            text.AppendLine()
                .Append("Player 基线    ").AppendLine(string.IsNullOrEmpty(startup.PlayerBaselineId) ? "—" : startup.PlayerBaselineId)
                .Append("发行渠道    ").AppendLine(string.IsNullOrEmpty(startup.ChannelId) ? "—" : startup.ChannelId)
                .Append("当前发行    ").AppendLine(string.IsNullOrEmpty(update.ReleaseId) ? "未选定" : update.ReleaseId)
                .Append("发行来源    ").AppendLine(ReleaseSourceLabel(update.ReleaseSource))
                .Append("热更阶段    ").AppendLine(HotUpdateStageLabel(update.Stage));
            if (!string.IsNullOrEmpty(update.Item))
                text.Append("阶段详情    ").AppendLine(update.Item);
            if (!string.IsNullOrEmpty(update.FallbackReason))
                text.Append("回退原因    ").AppendLine(update.FallbackReason);
            if (update.Error != null)
            {
                text.Append("热更错误    ").AppendLine(update.Error.Message);
                if (update.IsRestartRequired) text.Append("重试方式    退出进程后重新启动");
                else if (update.CanRetry) text.Append("重试方式    可在本次运行重试");
            }
            return text.ToString();
        }

        private static string StartupTaskLabel(TaskStatus state) => state switch
        {
            TaskStatus.RanToCompletion => "已就绪",
            TaskStatus.Faulted => "失败",
            TaskStatus.Canceled => "已取消",
            _ => "等待就绪",
        };

        private static string ReleaseSourceLabel(HotUpdateReleaseSource source) => source switch
        {
            HotUpdateReleaseSource.Bundled => "随包发行",
            HotUpdateReleaseSource.Channel => "在线渠道",
            HotUpdateReleaseSource.Confirmed => "已确认发行",
            _ => "未选定",
        };

        private static string HotUpdateStageLabel(HotUpdateStage stage) => stage switch
        {
            HotUpdateStage.Configuration => "配置校验",
            HotUpdateStage.ReleaseSelection => "选择发行",
            HotUpdateStage.ResourceUpdate => "更新资源",
            HotUpdateStage.PayloadValidation => "校验产物",
            HotUpdateStage.AotMetadata => "补充 AOT 元数据",
            HotUpdateStage.HotfixAssembly => "加载热更程序集",
            HotUpdateStage.Entry => "执行热更入口",
            HotUpdateStage.ReleaseConfirmation => "确认发行",
            HotUpdateStage.Ready => "已就绪",
            HotUpdateStage.Stopped => "已停止",
            _ => stage.ToString(),
        };

        private static string BuildAssetsText(RuntimeDebugSnapshot snapshot, AssetCacheSnapshot assets)
        {
            var text = new StringBuilder().Append("活跃租约    ").Append(assets.ActiveLeases).AppendLine()
                .Append("活跃资源    ").Append(assets.ActiveAssets).AppendLine()
                .Append("等待加载    ").Append(assets.PendingAssets).AppendLine()
                .Append("常驻资源    ").Append(assets.ResidentAssets).AppendLine()
                .Append("空闲资源    ").Append(assets.IdleAssets).AppendLine()
                .Append("活跃估算    ").Append(FormatBytes(assets.ActiveEstimatedBytes)).AppendLine()
                .Append("空闲估算    ").Append(FormatBytes(assets.IdleEstimatedBytes)).AppendLine().AppendLine();
            if (snapshot.AssetLeases.Count == 0) return text.Append("当前没有活跃租约。").ToString();

            text.AppendLine("地址与租约");
            foreach (AssetLeaseUsage lease in snapshot.AssetLeases)
                text.Append(lease.Address).Append("    租约 ").Append(lease.LeaseCount)
                    .Append("    ").Append(FormatBytes(lease.EstimatedBytes)).AppendLine();
            if (assets.ActiveAssets > snapshot.AssetLeases.Count)
                text.Append("还有 ").Append(assets.ActiveAssets - snapshot.AssetLeases.Count).Append(" 项资源");
            return text.ToString();
        }

        private static string BuildUIStateText(RuntimeDebugSnapshot snapshot)
        {
            var text = new StringBuilder().Append("已打开页面    ").Append(snapshot.UIOpenCount).AppendLine();
            foreach (UIPageResourceUsage page in snapshot.Pages)
                text.Append(page.ViewType?.Name ?? "未知页面")
                    .Append(page.Visible ? "    显示" : "    隐藏")
                    .Append("    租约 ").Append(page.Leases)
                    .Append("    ").Append(FormatBytes(page.EstimatedBytes)).AppendLine();
            text.AppendLine().Append("状态控制器    ").Append(snapshot.UIStateCount).AppendLine();
            foreach (RuntimeDebugUIState state in snapshot.UIStates)
                text.Append(state.ControllerName).Append(" #").Append(state.InstanceId)
                    .Append(state.IsActive ? "    启用" : "    停用")
                    .Append("    当前 ").Append(state.SelectedState)
                    .Append("    初态 ").Append(state.InitialState).AppendLine();
            if (snapshot.UIStateCount > snapshot.UIStates.Count)
                text.Append("还有 ").Append(snapshot.UIStateCount - snapshot.UIStates.Count).Append(" 个状态控制器");
            return text.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0L) return "—";
            if (bytes >= 1048576L) return $"{bytes / 1048576d:0.00} MiB";
            return $"{bytes / 1024d:0.00} KiB";
        }

        #endregion
    }
}
