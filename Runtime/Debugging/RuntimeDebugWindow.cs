using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private int _levelMask = 15;
        private int _infoCount;
        private int _warningCount;
        private int _errorCount;
        private int _fatalCount;
        private bool _dirty = true;
        private bool _isOpen;
        private bool _lockScroll = true;
        private int _group;
        private readonly int[] _subPages = new int[4];
        private Rect _iconRect = new Rect(10f, 10f, 120f, 78f);
        private Rect _windowRect = new Rect(10f, 10f, 680f, 520f);
        private float _windowScale = 1f;
        private Vector2 _pageScroll;
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
            if (_lockScroll) _logScroll.y = float.MaxValue;
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
                case 8: _fatalCount += delta; break;
            }
        }

        private static int Category(LogType type)
        {
            if (type == LogType.Warning) return 2;
            if (type == LogType.Exception) return 8;
            if (type == LogType.Error || type == LogType.Assert) return 4;
            return 1;
        }

        private void OnGUI()
        {
            GUI.depth = -10000;
            float scale = Mathf.Max(1f, Mathf.Min(Screen.width / 720f, Screen.height / 720f)) * _windowScale;
            Matrix4x4 originalMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                Rect safe = Screen.safeArea;
                Rect bounds = new Rect(safe.xMin / scale,
                    (Screen.height - safe.yMax) / scale,
                    safe.width / scale, safe.height / scale);
                if (!_isOpen)
                {
                    _iconRect = ClampWindow(_iconRect, bounds, 120f, 78f);
                    _iconRect = GUI.Window(41001, _iconRect, DrawIcon, "DEBUGGER");
                }
                else
                {
                    _windowRect = ClampWindow(_windowRect, bounds,
                        Mathf.Min(320f, bounds.width - 16f),
                        Mathf.Min(320f, bounds.height - 16f));
                    _windowRect = GUI.Window(41002, _windowRect, DrawWindow,
                        "EASY FRAMEWORK DEBUGGER");
                }
            }
            finally
            {
                GUI.matrix = originalMatrix;
            }
        }

        private static Rect ClampWindow(Rect rect, Rect bounds, float minimumWidth, float minimumHeight)
        {
            float maxWidth = Mathf.Max(1f, bounds.width - 16f);
            float maxHeight = Mathf.Max(1f, bounds.height - 16f);
            rect.width = Mathf.Clamp(rect.width, Mathf.Min(minimumWidth, maxWidth), maxWidth);
            rect.height = Mathf.Clamp(rect.height, Mathf.Min(minimumHeight, maxHeight), maxHeight);
            rect.x = Mathf.Clamp(rect.x, bounds.xMin + 8f, bounds.xMax - rect.width - 8f);
            rect.y = Mathf.Clamp(rect.y, bounds.yMin + 8f, bounds.yMax - rect.height - 8f);
            return rect;
        }

        private void DrawIcon(int windowId)
        {
            Color previous = GUI.color;
            if (_fatalCount > 0) GUI.color = new Color(0.95f, 0.35f, 0.35f);
            else if (_errorCount > 0) GUI.color = new Color(1f, 0.55f, 0.55f);
            else if (_warningCount > 0) GUI.color = new Color(1f, 0.85f, 0.45f);
            if (GUI.Button(new Rect(8f, 27f, _iconRect.width - 16f, 42f),
                    $"FPS: {FramesPerSecond:0.0}")) Open();
            GUI.color = previous;
            GUI.DragWindow(new Rect(0f, 0f, _iconRect.width, 25f));
        }

        private void DrawWindow(int windowId)
        {
            float width = _windowRect.width - 16f;
            int selected = GUI.Toolbar(new Rect(8f, 30f, width, 36f), _group,
                new[] { "Console", "Information", "Profiler", "Other", "Close" });
            if (selected == 4)
            {
                Close();
                return;
            }
            if (selected != _group) _pageScroll = Vector2.zero;
            _group = selected;

            float top = 72f;
            if (_group != 0)
            {
                string[] pages = _group == 1
                    ? new[] { "System", "Environment", "Screen", "Graphics" }
                    : _group == 2
                        ? new[] { "Summary", "Memory", "Scene" }
                        : new[] { "Settings", "Operations" };
                int page = GUI.Toolbar(new Rect(8f, top, width, 34f),
                    _subPages[_group], pages);
                if (page != _subPages[_group]) _pageScroll = Vector2.zero;
                _subPages[_group] = page;
                top += 40f;
            }

            Rect content = new Rect(8f, top, width, Mathf.Max(0f, _windowRect.height - top - 8f));
            switch (_group)
            {
                case 0: DrawConsole(content); break;
                case 1: DrawInformation(content, _subPages[1]); break;
                case 2: DrawProfiler(content, _subPages[2]); break;
                case 3: DrawOther(content, _subPages[3]); break;
            }
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 26f));
        }

        private float FramesPerSecond => _smoothedDeltaTime > 0f ? 1f / _smoothedDeltaTime : 0f;

        private void DrawConsole(Rect area)
        {
            if (GUI.Button(new Rect(area.x, area.y, 82f, 34f), "Clear All")) Clear();
            _lockScroll = GUI.Toggle(new Rect(area.x + 88f, area.y, 110f, 34f),
                _lockScroll, "Lock Scroll");
            if (GUI.Button(new Rect(area.x + 204f, area.y, 85f, 34f), "Copy All")) CopyAll();

            float filterWidth = (area.width - 12f) / 4f;
            DrawFilter(new Rect(area.x, area.y + 40f, filterWidth, 34f), 1, $"Info ({_infoCount})");
            DrawFilter(new Rect(area.x + filterWidth + 4f, area.y + 40f, filterWidth, 34f), 2,
                $"Warning ({_warningCount})");
            DrawFilter(new Rect(area.x + (filterWidth + 4f) * 2f, area.y + 40f, filterWidth, 34f), 4,
                $"Error ({_errorCount})");
            DrawFilter(new Rect(area.x + (filterWidth + 4f) * 3f, area.y + 40f, filterWidth, 34f), 8,
                $"Fatal ({_fatalCount})");

            GUI.Label(new Rect(area.x, area.y + 82f, 58f, 30f), "Search");
            _search = GUI.TextField(new Rect(area.x + 58f, area.y + 78f, area.width - 58f, 34f),
                _search ?? string.Empty);
            if (_dirty || !string.Equals(_search, _appliedSearch, StringComparison.Ordinal))
                RebuildVisibleIndices();

            float listHeight = Mathf.Max(50f, (area.height - 122f) * 0.52f);
            Rect listRect = new Rect(area.x, area.y + 118f, area.width, listHeight);
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
                else if (Category(entry.Type) == 8) GUI.color = new Color(0.95f, 0.45f, 0.45f);
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

        private void DrawInformation(Rect area, int page)
        {
            string info;
            switch (page)
            {
                case 1:
                    info = "ENVIRONMENT\n\n" +
                        $"Platform: {Application.platform}\n" +
                        $"Product: {Application.productName}\n" +
                        $"Company: {Application.companyName}\n" +
                        $"Identifier: {Application.identifier}\n" +
                        $"Version: {Application.version}\n" +
                        $"Data Path: {Application.dataPath}\n" +
                        $"Persistent Path: {Application.persistentDataPath}\n" +
                        $"Temporary Path: {Application.temporaryCachePath}\n" +
                        $"Runtime Mode: {Runtime.Mode}";
                    break;
                case 2:
                    Rect safe = Screen.safeArea;
                    info = "SCREEN\n\n" +
                        $"Size: {Screen.width} × {Screen.height}\n" +
                        $"DPI: {Screen.dpi:0.0}\n" +
                        $"Orientation: {Screen.orientation}\n" +
                        $"Safe Area: {safe.x:0}, {safe.y:0}, {safe.width:0}, {safe.height:0}\n" +
                        $"Fullscreen: {Screen.fullScreen}\n" +
                        $"Display Resolution: {Screen.currentResolution}";
                    break;
                case 3:
                    info = "GRAPHICS\n\n" +
                        $"Device: {SystemInfo.graphicsDeviceName}\n" +
                        $"Type: {SystemInfo.graphicsDeviceType}\n" +
                        $"Version: {SystemInfo.graphicsDeviceVersion}\n" +
                        $"Memory: {SystemInfo.graphicsMemorySize} MB\n" +
                        $"Shader Level: {SystemInfo.graphicsShaderLevel}\n" +
                        $"Max Texture Size: {SystemInfo.maxTextureSize}\n" +
                        $"Instancing: {SystemInfo.supportsInstancing}\n" +
                        $"Compute Shaders: {SystemInfo.supportsComputeShaders}";
                    break;
                default:
                    info = "SYSTEM\n\n" +
                        $"Device: {SystemInfo.deviceName}\n" +
                        $"Model: {SystemInfo.deviceModel}\n" +
                        $"OS: {SystemInfo.operatingSystem}\n" +
                        $"Processor: {SystemInfo.processorType}\n" +
                        $"Cores: {SystemInfo.processorCount}\n" +
                        $"System Memory: {SystemInfo.systemMemorySize} MB\n" +
                        $"Unity: {Application.unityVersion}\n" +
                        $"Development Build: {Debug.isDebugBuild}";
                    break;
            }
            DrawTextPage(area, info);
        }

        private void DrawProfiler(Rect area, int page)
        {
            string info;
            switch (page)
            {
                case 1:
                    info = "MEMORY\n\n" +
                        $"Managed Heap Used: {FormatBytes(GC.GetTotalMemory(false))}\n" +
                        $"System Memory: {SystemInfo.systemMemorySize} MB\n" +
                        $"Graphics Memory: {SystemInfo.graphicsMemorySize} MB\n" +
                        $"Buffered Logs: {_entries.Count} / {Capacity}";
                    break;
                case 2:
                    var scenes = new StringBuilder("SCENE\n\n");
                    Scene active = SceneManager.GetActiveScene();
                    scenes.Append("Active: ").Append(active.name).Append('\n');
                    scenes.Append("Loaded Scenes: ").Append(SceneManager.sceneCount).Append("\n\n");
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene scene = SceneManager.GetSceneAt(i);
                        scenes.Append(i + 1).Append(". ").Append(scene.name)
                            .Append(" (build index ").Append(scene.buildIndex).Append(")\n");
                    }
                    info = scenes.ToString();
                    break;
                default:
                    float frameMs = _smoothedDeltaTime * 1000f;
                    info = "PROFILER SUMMARY\n\n" +
                        $"FPS: {FramesPerSecond:0.0}\n" +
                        $"Frame Time: {frameMs:0.0} ms\n" +
                        $"Frame Count: {Time.frameCount}\n" +
                        $"Time Scale: {Time.timeScale:0.00}\n" +
                        $"Target FPS: {Application.targetFrameRate}\n" +
                        $"VSync Count: {QualitySettings.vSyncCount}\n" +
                        $"Quality: {QualitySettings.names[QualitySettings.GetQualityLevel()]}";
                    break;
            }
            DrawTextPage(area, info);
        }

        private static string FormatBytes(long bytes)
        {
            return bytes >= 1048576L
                ? $"{bytes / 1048576f:0.0} MB"
                : $"{bytes / 1024f:0.0} KB";
        }

        private void DrawTextPage(Rect area, string info)
        {
            GUI.Box(area, GUIContent.none);
            float viewWidth = Mathf.Max(1f, area.width - 20f);
            float textWidth = Mathf.Max(1f, viewWidth - 20f);
            float textHeight = DetailStyle.CalcHeight(new GUIContent(info), textWidth);
            _pageScroll = GUI.BeginScrollView(area, _pageScroll,
                new Rect(0f, 0f, viewWidth, Mathf.Max(area.height, textHeight + 24f)));
            GUI.Label(new Rect(10f, 10f, textWidth, textHeight), info, DetailStyle);
            GUI.EndScrollView();
        }

        private void DrawOther(Rect area, int page)
        {
            GUI.Box(area, GUIContent.none);
            float x = area.x + 12f;
            float y = area.y + 10f;
            if (page == 0)
            {
                GUI.Label(new Rect(x, y, area.width - 24f, 30f), "WINDOW SETTINGS");
                GUI.Label(new Rect(x, y + 36f, area.width - 24f, 28f),
                    "Drag the title bar to move the window.");
                GUI.Label(new Rect(x, y + 72f, 105f, 32f), $"Scale: {_windowScale:0.00}");
                if (GUI.Button(new Rect(x + 110f, y + 70f, 54f, 34f), "-"))
                    _windowScale = Mathf.Max(0.75f, _windowScale - 0.1f);
                if (GUI.Button(new Rect(x + 170f, y + 70f, 54f, 34f), "+"))
                    _windowScale = Mathf.Min(1.5f, _windowScale + 0.1f);
                if (GUI.Button(new Rect(x, y + 116f, 120f, 34f), "Width −"))
                    _windowRect.width -= 40f;
                if (GUI.Button(new Rect(x + 126f, y + 116f, 120f, 34f), "Width +"))
                    _windowRect.width += 40f;
                if (GUI.Button(new Rect(x, y + 158f, 120f, 34f), "Height −"))
                    _windowRect.height -= 40f;
                if (GUI.Button(new Rect(x + 126f, y + 158f, 120f, 34f), "Height +"))
                    _windowRect.height += 40f;
                if (GUI.Button(new Rect(x, y + 204f, 246f, 36f), "Reset Window Layout"))
                {
                    _windowScale = 1f;
                    _iconRect = new Rect(10f, 10f, 120f, 78f);
                    _windowRect = new Rect(10f, 10f, 680f, 520f);
                }
            }
            else
            {
                GUI.Label(new Rect(x, y, area.width - 24f, 30f), "OPERATIONS");
                if (GUI.Button(new Rect(x, y + 40f, 220f, 38f), "Copy All Logs")) CopyAll();
                if (GUI.Button(new Rect(x, y + 86f, 220f, 38f), "Clear Logs")) Clear();
                if (GUI.Button(new Rect(x, y + 132f, 220f, 38f), "Collect Garbage")) GC.Collect();
                if (GUI.Button(new Rect(x, y + 178f, 220f, 38f), "Unload Unused Assets"))
                    Resources.UnloadUnusedAssets();
            }
        }

        private void CopyAll()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                LogEntry entry = _entries[i];
                text.Append('[').Append(entry.Time).Append("] ").Append(entry.Type)
                    .Append(": ").Append(entry.Message).Append('\n');
                if (!string.IsNullOrEmpty(entry.StackTrace))
                    text.Append(entry.StackTrace).Append('\n');
            }
            GUIUtility.systemCopyBuffer = text.ToString();
        }

        private void Clear()
        {
            _entries.Clear();
            _visibleIndices.Clear();
            _selectedIndex = -1;
            _infoCount = _warningCount = _errorCount = _fatalCount = 0;
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
