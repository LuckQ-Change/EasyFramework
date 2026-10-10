using System;
using System.Collections.Generic;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace EasyFramework
{
    /// <summary>在设备上查看 Unity 与框架日志的轻量调试窗口。</summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeDebugWindow : MonoBehaviour
    {
        private const int Capacity = 500;
        private const int MaxMessageLength = 4096;
        private const int MaxStackLength = 16384;
        private const float RowHeight = 36f;

        private struct LogEntry
        {
            public string Time;
            public string Message;
            public string StackTrace;
            public LogType Type;
        }

        private static readonly List<LogEntry> EarlyEntries = new List<LogEntry>(Capacity);
        private readonly List<LogEntry> _entries = new List<LogEntry>(Capacity);
        private readonly List<int> _visibleIndices = new List<int>(Capacity);
        private Vector2 _logScroll;
        private Vector2 _detailScroll;
        private string _search = string.Empty;
        private string _appliedSearch = string.Empty;
        private int _selectedIndex = -1;
        private int _levelMask = 7;
        private int _infoCount;
        private int _warningCount;
        private int _errorCount;
        private bool _dirty = true;
        private bool _isOpen;
        private bool _showSystem;
        private float _smoothedDeltaTime;
        private GUIStyle _rowStyle;
        private GUIStyle _detailStyle;

        public static RuntimeDebugWindow Instance { get; private set; }
        public bool IsOpen => _isOpen;

        public void Open() => _isOpen = true;
        public void Close() => _isOpen = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CaptureStartupLogs()
        {
            Application.logMessageReceived -= OnEarlyLogMessage;
            EarlyEntries.Clear();
            Application.logMessageReceived += OnEarlyLogMessage;
        }

        private static void OnEarlyLogMessage(string message, string stackTrace, LogType type)
        {
            if (EarlyEntries.Count == Capacity) EarlyEntries.RemoveAt(0);
            EarlyEntries.Add(CreateEntry(message, stackTrace, type));
        }

        private static void StopStartupCapture()
        {
            Application.logMessageReceived -= OnEarlyLogMessage;
            EarlyEntries.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureForProjectsWithoutLauncher()
        {
            if (Instance != null || FrameworkLauncher.Instance != null)
            {
                StopStartupCapture();
                return;
            }
            FrameworkStartupConfig config = FrameworkStartupConfig.Resolve(null);
            if (!config.ShowDebugWindow)
            {
                StopStartupCapture();
                return;
            }

            var host = new GameObject("[EasyFramework Debug]");
            DontDestroyOnLoad(host);
            host.AddComponent<RuntimeDebugWindow>();
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Application.logMessageReceived -= OnEarlyLogMessage;
            for (int i = 0; i < EarlyEntries.Count; i++) AddEntry(EarlyEntries[i]);
            EarlyEntries.Clear();
            Application.logMessageReceived += OnLogMessage;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (delta > 0f)
                _smoothedDeltaTime = _smoothedDeltaTime <= 0f
                    ? delta
                    : Mathf.Lerp(_smoothedDeltaTime, delta, 0.1f);
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            AddEntry(CreateEntry(message, stackTrace, type));
        }

        private static LogEntry CreateEntry(string message, string stackTrace, LogType type)
        {
            return new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Message = Limit(message, MaxMessageLength),
                StackTrace = Limit(stackTrace, MaxStackLength),
                Type = type,
            };
        }

        private void AddEntry(LogEntry entry)
        {
            if (_entries.Count == Capacity)
            {
                Count(_entries[0].Type, -1);
                _entries.RemoveAt(0);
                if (_selectedIndex >= 0) _selectedIndex--;
            }

            _entries.Add(entry);
            Count(entry.Type, 1);
            _dirty = true;
        }

        private static string Limit(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= length ? value : value.Substring(0, length) + "…";
        }

        private void Count(LogType type, int delta)
        {
            switch (Category(type))
            {
                case 1: _infoCount += delta; break;
                case 2: _warningCount += delta; break;
                case 4: _errorCount += delta; break;
            }
        }

        private static int Category(LogType type)
        {
            if (type == LogType.Warning) return 2;
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) return 4;
            return 1;
        }

        private void OnGUI()
        {
            GUI.depth = -10000;
            float scale = Mathf.Max(1f, Mathf.Min(Screen.width / 720f, Screen.height / 720f));
            Matrix4x4 originalMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                Rect safe = Screen.safeArea;
                float x = safe.xMin / scale;
                float y = (Screen.height - safe.yMax) / scale;
                float width = safe.width / scale;
                float height = safe.height / scale;
                if (!_isOpen)
                {
                    if (GUI.Button(new Rect(x + 8f, y + 8f, 108f, 48f),
                            $"DEBUG  {_errorCount}", GUI.skin.button))
                        Open();
                    return;
                }

                DrawPanel(new Rect(x + 8f, y + 8f, width - 16f, height - 16f));
            }
            finally
            {
                GUI.matrix = originalMatrix;
            }
        }

        private void DrawPanel(Rect panel)
        {
            if (panel.width < 300f || panel.height < 260f) return;
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, panel.width - 120f, 32f),
                $"EasyFramework Debug    {FramesPerSecond:0} FPS");
            if (GUI.Button(new Rect(panel.xMax - 58f, panel.y + 6f, 48f, 40f), "×")) Close();

            float left = panel.x + 10f;
            float contentWidth = panel.width - 20f;
            float tabsY = panel.y + 50f;
            if (GUI.Button(new Rect(left, tabsY, 110f, 40f), "Console")) _showSystem = false;
            if (GUI.Button(new Rect(left + 116f, tabsY, 110f, 40f), "System")) _showSystem = true;

            if (_showSystem) DrawSystem(new Rect(left, tabsY + 50f, contentWidth, panel.height - 110f));
            else DrawConsole(new Rect(left, tabsY + 50f, contentWidth, panel.height - 110f));
        }

        private float FramesPerSecond => _smoothedDeltaTime > 0f ? 1f / _smoothedDeltaTime : 0f;

        private void DrawConsole(Rect area)
        {
            float buttonWidth = Mathf.Max(58f, (area.width - 70f) / 4f);
            DrawFilter(new Rect(area.x, area.y, buttonWidth, 38f), 1, $"Info {_infoCount}");
            DrawFilter(new Rect(area.x + buttonWidth + 4f, area.y, buttonWidth, 38f), 2,
                $"Warn {_warningCount}");
            DrawFilter(new Rect(area.x + (buttonWidth + 4f) * 2f, area.y, buttonWidth, 38f), 4,
                $"Error {_errorCount}");
            if (GUI.Button(new Rect(area.xMax - 62f, area.y, 62f, 38f), "Clear")) Clear();

            GUI.Label(new Rect(area.x, area.y + 45f, 58f, 32f), "Search");
            _search = GUI.TextField(new Rect(area.x + 58f, area.y + 43f, area.width - 58f, 38f),
                _search ?? string.Empty);
            if (_dirty || !string.Equals(_search, _appliedSearch, StringComparison.Ordinal))
                RebuildVisibleIndices();

            float listHeight = Mathf.Max(60f, (area.height - 90f) * 0.55f);
            Rect listRect = new Rect(area.x, area.y + 88f, area.width, listHeight);
            DrawLogList(listRect);

            float detailY = listRect.yMax + 6f;
            Rect detailRect = new Rect(area.x, detailY, area.width, Mathf.Max(0f, area.yMax - detailY));
            DrawDetail(detailRect);
        }

        private void DrawFilter(Rect rect, int bit, string label)
        {
            bool enabled = (_levelMask & bit) != 0;
            Color previous = GUI.color;
            if (!enabled) GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            if (GUI.Button(rect, label))
            {
                _levelMask ^= bit;
                _dirty = true;
            }
            GUI.color = previous;
        }

        private void RebuildVisibleIndices()
        {
            _visibleIndices.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                LogEntry entry = _entries[i];
                if ((_levelMask & Category(entry.Type)) == 0) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    entry.Message.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.StackTrace.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _visibleIndices.Add(i);
            }
            _appliedSearch = _search;
            _dirty = false;
        }

        private void DrawLogList(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);
            float viewWidth = Mathf.Max(1f, rect.width - 20f);
            float viewHeight = Mathf.Max(rect.height, _visibleIndices.Count * RowHeight);
            _logScroll = GUI.BeginScrollView(rect, _logScroll,
                new Rect(0f, 0f, viewWidth, viewHeight));
            int first = Mathf.Max(0, Mathf.FloorToInt(_logScroll.y / RowHeight));
            int last = Mathf.Min(_visibleIndices.Count,
                Mathf.CeilToInt((_logScroll.y + rect.height) / RowHeight) + 1);
            for (int row = first; row < last; row++)
            {
                int index = _visibleIndices[row];
                LogEntry entry = _entries[index];
                Rect rowRect = new Rect(2f, row * RowHeight, viewWidth - 4f, RowHeight - 2f);
                Color previous = GUI.color;
                if (index == _selectedIndex) GUI.color = new Color(0.65f, 0.85f, 1f);
                else if (Category(entry.Type) == 4) GUI.color = new Color(1f, 0.65f, 0.65f);
                else if (Category(entry.Type) == 2) GUI.color = new Color(1f, 0.85f, 0.55f);
                string line = entry.Message.Replace('\n', ' ').Replace('\r', ' ');
                if (GUI.Button(rowRect, $"{entry.Time}  {line}", RowStyle))
                {
                    _selectedIndex = index;
                    _detailScroll = Vector2.zero;
                }
                GUI.color = previous;
            }
            GUI.EndScrollView();
        }

        private void DrawDetail(Rect rect)
        {
            GUI.Box(rect, GUIContent.none);
            if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 28f),
                    "Tap a log to view details and stack trace.");
                return;
            }

            LogEntry entry = _entries[_selectedIndex];
            if (GUI.Button(new Rect(rect.xMax - 72f, rect.y + 4f, 64f, 32f), "Copy"))
                GUIUtility.systemCopyBuffer = entry.Message + "\n" + entry.StackTrace;
            string detail = entry.Time + "  " + entry.Type + "\n" + entry.Message +
                (string.IsNullOrEmpty(entry.StackTrace) ? string.Empty : "\n\n" + entry.StackTrace);
            Rect body = new Rect(rect.x + 6f, rect.y + 38f, rect.width - 12f,
                Mathf.Max(0f, rect.height - 44f));
            float textWidth = Mathf.Max(1f, body.width - 20f);
            float textHeight = Mathf.Max(body.height, DetailStyle.CalcHeight(new GUIContent(detail), textWidth) + 8f);
            _detailScroll = GUI.BeginScrollView(body, _detailScroll,
                new Rect(0f, 0f, textWidth, textHeight));
            GUI.Label(new Rect(4f, 4f, textWidth - 8f, textHeight - 8f), detail, DetailStyle);
            GUI.EndScrollView();
        }

        private void DrawSystem(Rect area)
        {
            GUI.Box(area, GUIContent.none);
            string info =
                $"Device: {SystemInfo.deviceModel}\n" +
                $"OS: {SystemInfo.operatingSystem}\n" +
                $"Unity: {Application.unityVersion}\n" +
                $"App: {Application.identifier}  {Application.version}\n" +
                $"Resolution: {Screen.width} × {Screen.height}\n" +
                $"Graphics: {SystemInfo.graphicsDeviceName}\n" +
                $"Memory: {SystemInfo.systemMemorySize} MB\n" +
                $"FPS: {FramesPerSecond:0.0}\n" +
                $"Runtime: {Runtime.Mode}\n" +
                $"Development Build: {Debug.isDebugBuild}";
            GUI.Label(new Rect(area.x + 12f, area.y + 12f, area.width - 24f, area.height - 24f),
                info, DetailStyle);
        }

        private void Clear()
        {
            _entries.Clear();
            _visibleIndices.Clear();
            _selectedIndex = -1;
            _infoCount = _warningCount = _errorCount = 0;
            _logScroll = _detailScroll = Vector2.zero;
            _dirty = true;
        }

        private GUIStyle RowStyle
        {
            get
            {
                if (_rowStyle == null)
                {
                    _rowStyle = new GUIStyle(GUI.skin.button)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        fontSize = 14,
                    };
                }
                return _rowStyle;
            }
        }

        private GUIStyle DetailStyle
        {
            get
            {
                if (_detailStyle == null)
                {
                    _detailStyle = new GUIStyle(GUI.skin.label)
                    {
                        wordWrap = true,
                        alignment = TextAnchor.UpperLeft,
                        fontSize = 15,
                    };
                }
                return _detailStyle;
            }
        }
    }
}
