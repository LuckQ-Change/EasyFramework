using System;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework.Editor.UI
{
    /// <summary>编辑、预览 UITimeline 动画并管理工作台的时间视区。</summary>
    internal sealed class UITimelineWorkbench : EditorWindow
    {
        private const float LABEL_WIDTH = 156f;
        private const float RULER_HEIGHT = 24f;
        private const float ROW_HEIGHT = 23f;
        private const float MIN_TICK_SPACING = 72f;
        private const float MIN_OVERVIEW_DURATION = 0.1f;
        private const float MAX_ZOOM = 128f;
        private const float WHEEL_ZOOM_FACTOR = 1.12f;
        private const float MAX_DRAG_SNAP = 0.01f;
        private const float DRAG_SNAP_SPACING = 4f;
        private static readonly GUIContent _fitTimelineContent = new("适应整段", "时间区滚轮缩放，中键平移，F 适应整段");
        private static readonly GUIContent _eventLaneContent = new("事件轨", "点击标记编辑事件。重叠标记可在事件列表中选择。");
        private static readonly Color _playheadColor = new(0.96f, 0.8f, 0.32f);
        private static readonly Color _eventMarkerColor = new(0.42f, 0.83f, 0.66f);
        private static readonly Color _selectedLaneColor = new(0.26f, 0.32f, 0.4f);
        private static readonly Color _laneColor = new(0.15f, 0.18f, 0.21f);
        private static UITimelineWorkbench _activeWindow;

        private UITimeline _timeline;
        private SerializedObject _serialized;
        private UITimelinePlayback _preview;
        private Vector2 _windowScroll;
        private Vector2 _scroll;
        private float _viewStart;
        private float _viewDuration;
        private int _animation;
        private int _row;
        private int _key;
        private int _eventIndex = -1;
        private int _eventDragControl;
        private float _eventDragTime;
        private bool _isEventLaneSelected;
        private bool _track;
        private bool _playing;
        private float _seek;
        private double _lastTick;
        private string _error;
        private string _validationError;
        private bool _validate;
        private float _dragX;
        private float _dragTime;
        private float _dragExtent;
        private float _dragSecondsPerPoint;
        private float _dragSnapStep;
        private bool _isResizing;

        /// <summary>标尺、轨道和输入共用的可见时间范围。</summary>
        private readonly struct TimeView
        {
            internal float Start { get; }
            internal float Duration { get; }
            internal float End => Start + Duration;

            internal TimeView(float start, float duration)
            {
                Start = start;
                Duration = duration;
            }

            internal float TimeToX(Rect lane, float seconds) => lane.x + (seconds - Start) / Duration * lane.width;

            internal float XToTime(Rect lane, float x) => Start + Mathf.Clamp01((x - lane.x) / lane.width) * Duration;
        }

        #region 生命周期

        [MenuItem("Tools/EasyFramework/UI/Tween Timeline", false, 21)]
        private static void OpenMenu() => Open(Selection.activeGameObject == null
            ? null : Selection.activeGameObject.GetComponentInParent<UITimeline>());

        internal static void Open(UITimeline timeline)
        {
            var window = GetWindow<UITimelineWorkbench>("UI Timeline");
            window.minSize = new Vector2(620f, 400f);
            window.SetTimeline(timeline);
            window.Show();
        }

        private void OnEnable()
        {
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += TickPreview;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= TickPreview;
            Undo.undoRedoPerformed -= OnUndoRedo;
            CancelEventDrag();
            StopPreview();
            _serialized?.Dispose();
            _serialized = null;
        }

        private void OnLostFocus() => CancelEventDrag();

        private void OnUndoRedo()
        {
            CancelEventDrag();
            _eventIndex = -1;
            _validate = true;
            StopPreview();
        }

        #endregion

        #region 私有逻辑

        private void SetTimeline(UITimeline timeline)
        {
            StopPreview();
            _serialized?.Dispose();
            _timeline = timeline;
            _serialized = timeline == null ? null : new SerializedObject(timeline);
            SelectAnimation(0);
        }

        private void SelectAnimation(int index)
        {
            StopPreview();
            _animation = index;
            _row = -1;
            _key = -1;
            CancelEventDrag();
            _eventIndex = -1;
            _isEventLaneSelected = false;
            _seek = 0f;
            _error = null;
            _validate = true;
            _scroll = Vector2.zero;
            FitTimeline();
        }

        private void OnGUI()
        {
            Rect timelineField = EditorGUILayout.GetControlRect();
            var timeline = (UITimeline)EditorGUI.ObjectField(timelineField, "时间轴", _timeline, typeof(UITimeline), true);
            if (timeline != _timeline) SetTimeline(timeline);
            if (timeline == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("在编辑模式选择带 UITimeline 的节点。", MessageType.Info);
                return;
            }
            _serialized ??= new SerializedObject(timeline);
            // 记录外层滚动区域的入口，避免滚到屏幕外的轨道仍响应指针。
            var windowContents = Rect.MinMaxRect(0f, timelineField.yMax, position.width, position.height);
            bool isPointerInContents = windowContents.Contains(Event.current.mousePosition);
            using var contents = new EditorGUILayout.ScrollViewScope(_windowScroll);
            _windowScroll = contents.scrollPosition;
            _serialized.Update();
            var animations = _serialized.FindProperty("_animations");
            DrawAnimationSelector(animations);

            if (animations.arraySize == 0)
            {
                _serialized.ApplyModifiedProperties();
                return;
            }

            var current = animations.GetArrayElementAtIndex(_animation);
            var clips = current.FindPropertyRelative("_clips");
            var tracks = current.FindPropertyRelative("_tracks");
            var events = current.FindPropertyRelative("_events");
            EditorGUILayout.PropertyField(_serialized.FindProperty("_timeMode"), new GUIContent("计时方式"));
            DrawTracks(clips, tracks, events, isPointerInContents);
            if (_isEventLaneSelected) DrawSelectedEvent(events);
            else DrawSelectedRow(clips, tracks);
            if (_serialized.ApplyModifiedProperties())
            {
                if (_preview != null) RebuildPreview();
                _validate = true;
            }
            if (_validate)
            {
                _validate = false;
                try
                {
                    _timeline.CreatePlayback(_timeline.Animations[_animation]);
                    _validationError = null;
                }
                catch (InvalidOperationException error) { _validationError = error.Message; }
            }
            DrawPreviewControls();
            string errorMessage = _error ?? _validationError;
            if (!string.IsNullOrEmpty(errorMessage)) EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
        }

        private void DrawAnimationSelector(SerializedProperty animations)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (animations.arraySize > 0)
                {
                    string[] names = Enumerable.Range(0, animations.arraySize)
                        .Select(index => animations.GetArrayElementAtIndex(index).FindPropertyRelative("_name").stringValue)
                        .ToArray();
                    _animation = Mathf.Clamp(_animation, 0, names.Length - 1);
                    int selected = EditorGUILayout.Popup("动画", _animation, names);
                    if (selected != _animation) SelectAnimation(selected);
                    if (GUILayout.Button("删除", GUILayout.Width(52f)))
                    {
                        SetPageTransition(names[_animation], null);
                        animations.DeleteArrayElementAtIndex(_animation);
                        SelectAnimation(Mathf.Min(_animation, animations.arraySize - 1));
                    }
                }
                if (GUILayout.Button("新建动画", GUILayout.Width(78f)))
                {
                    int index = animations.arraySize;
                    var entry = Append(animations);
                    string name = "Animation";
                    int suffix = 2;
                    while (NameExists(animations, name, index)) name = $"Animation {suffix++}";
                    entry.FindPropertyRelative("_name").stringValue = name;
                    entry.FindPropertyRelative("_clips").ClearArray();
                    entry.FindPropertyRelative("_tracks").ClearArray();
                    entry.FindPropertyRelative("_events").ClearArray();
                    SelectAnimation(index);
                }
            }

            if (animations.arraySize == 0) return;
            var animationName = animations.GetArrayElementAtIndex(_animation).FindPropertyRelative("_name");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField("名称", animationName.stringValue).Trim();
                if (EditorGUI.EndChangeCheck() && name != animationName.stringValue)
                {
                    if (name.Length == 0 || NameExists(animations, name, _animation))
                        _error = "动画需要唯一的名称。";
                    else
                    {
                        SetPageTransition(animationName.stringValue, name);
                        animationName.stringValue = name;
                        _error = null;
                    }
                }

                UIDisplay page = _timeline.GetComponentInParent<UIDisplay>(true);
                if (page != null && page.IsPage)
                {
                    DrawPageAssignment(page, animationName.stringValue, opening: true);
                    DrawPageAssignment(page, animationName.stringValue, opening: false);
                }
            }
        }

        private static bool NameExists(SerializedProperty animations, string name, int except)
        {
            for (int i = 0; i < animations.arraySize; i++)
                if (i != except && animations.GetArrayElementAtIndex(i).FindPropertyRelative("_name").stringValue == name)
                    return true;
            return false;
        }

        private void DrawPageAssignment(UIDisplay page, string name, bool opening)
        {
            string prefix = opening ? "_open" : "_close";
            var transition = page.GetComponent<UITransition>();
            bool assigned = false;
            if (transition != null)
            {
                using var configuration = new SerializedObject(transition);
                assigned = configuration.FindProperty(prefix + "Timeline").objectReferenceValue == _timeline &&
                           configuration.FindProperty(prefix + "Animation").stringValue == name;
            }
            string usage = opening ? "开场" : "退场";
            if (!GUILayout.Button(assigned ? $"取消{usage}" : $"设为{usage}", GUILayout.Width(73f))) return;
            if (transition == null) transition = Undo.AddComponent<UITransition>(page.gameObject);
            using var data = new SerializedObject(transition);
            data.FindProperty(prefix + "Timeline").objectReferenceValue = assigned ? null : _timeline;
            data.FindProperty(prefix + "Animation").stringValue = assigned ? string.Empty : name;
            data.ApplyModifiedProperties();
        }

        private void SetPageTransition(string previous, string replacement)
        {
            var page = _timeline.GetComponentInParent<UIDisplay>(true);
            if (page == null || !page.TryGetComponent(out UITransition transition)) return;
            using var data = new SerializedObject(transition);
            Rename("_open");
            Rename("_close");
            data.ApplyModifiedProperties();

            void Rename(string prefix)
            {
                var source = data.FindProperty(prefix + "Timeline");
                var name = data.FindProperty(prefix + "Animation");
                if (source.objectReferenceValue != _timeline || name.stringValue != previous) return;
                name.stringValue = replacement ?? string.Empty;
                if (replacement == null) source.objectReferenceValue = null;
            }
        }

        private void DrawTracks(SerializedProperty clips, SerializedProperty tracks, SerializedProperty events,
            bool isPointerInContents)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("添加片段"))
                {
                    var row = Append(clips);
                    SetDefaultBinding(row);
                    SetDuration(row, "_start", _seek);
                    SetDuration(row, "_duration", 0.3f);
                    row.FindPropertyRelative("_ease").enumValueIndex = 0;
                    var binding = GetBinding(row);
                    var value = binding.Target == null ? Vector4.zero : binding.Read();
                    row.FindPropertyRelative("_from").vector4Value = value;
                    row.FindPropertyRelative("_to").vector4Value = value;
                    SelectRow(false, clips.arraySize - 1);
                }
                if (GUILayout.Button("添加关键帧轨道"))
                {
                    var row = Append(tracks);
                    SetDefaultBinding(row);
                    var keys = row.FindPropertyRelative("_keys");
                    keys.ClearArray();
                    SelectRow(true, tracks.arraySize - 1);
                    RecordKey(row, _seek);
                }
                if (GUILayout.Button("游标处添加事件"))
                {
                    int index = events.arraySize;
                    var marker = Append(events);
                    SetDuration(marker, "_time", _seek);
                    marker.FindPropertyRelative("_eventName").stringValue = "Event";
                    SelectEvent(index);
                    _scroll.y = 0f;
                }
                if (GUILayout.Button(_fitTimelineContent, GUILayout.Width(78f)))
                    FitTimeline();
            }

            float duration = ValidSeconds(Length(clips, tracks, events));
            float extent = GUIUtility.hotControl != 0 && _dragExtent > 0f
                ? _dragExtent : Mathf.Max(MIN_OVERVIEW_DURATION, duration, ValidSeconds(1.12f * duration), _seek);
            TimeView view = GetTimeView(extent);
            float pinnedHeight = RULER_HEIGHT + ROW_HEIGHT;
            float propertyHeight = ROW_HEIGHT * (clips.arraySize + tracks.arraySize);
            Rect viewport = GUILayoutUtility.GetRect(110f, Mathf.Clamp(pinnedHeight + propertyHeight, 146f, 260f),
                GUILayout.ExpandWidth(true));
            var propertyViewport = new Rect(viewport.x, viewport.y + pinnedHeight, viewport.width,
                Mathf.Max(0f, viewport.height - pinnedHeight));
            GUIStyle scrollbar = GUI.skin.verticalScrollbar;
            float maxScroll = Mathf.Max(0f, propertyHeight - propertyViewport.height);
            float scrollbarWidth = maxScroll > 0f ? scrollbar.fixedWidth + scrollbar.margin.left : 0f;
            float contentWidth = Mathf.Max(0f, viewport.width - scrollbarWidth);
            var visibleArea = new Rect(viewport.x, viewport.y, contentWidth, viewport.height);
            bool isPointerOverTracks = isPointerInContents && visibleArea.Contains(Event.current.mousePosition);
            var visibleProperties = new Rect(propertyViewport.x, propertyViewport.y, contentWidth, propertyViewport.height);
            bool isPointerOverProperties = isPointerOverTracks && visibleProperties.Contains(Event.current.mousePosition);

            // 用同一个可见轨道区域处理导航，先于内外滚动视图消费时间区的滚轮。
            HandleViewInput(Lane(visibleArea), view, extent, isPointerOverTracks);
            if (maxScroll > 0f && isPointerOverTracks && !isPointerOverProperties &&
                Event.current.type == EventType.ScrollWheel)
            {
                // 固定行的侧栏仍把滚轮交给属性轨道的纵向滚动。
                _scroll.y = Mathf.Clamp(_scroll.y + Event.current.delta.y * ROW_HEIGHT, 0f, maxScroll);
                Event.current.Use();
            }
            DrawRuler(new Rect(viewport.x, viewport.y, contentWidth, RULER_HEIGHT), view, isPointerOverTracks);
            DrawEventLane(new Rect(viewport.x, viewport.y + RULER_HEIGHT, contentWidth, ROW_HEIGHT), events, view,
                extent, isPointerOverTracks);

            _scroll = GUI.BeginScrollView(propertyViewport, _scroll, new Rect(0f, 0f, contentWidth, propertyHeight),
                false, false, GUIStyle.none, scrollbar);
            try
            {
                var rowArea = new Rect(0f, 0f, contentWidth, ROW_HEIGHT);
                for (int i = 0; i < clips.arraySize; i++)
                {
                    DrawLane(rowArea, clips.GetArrayElementAtIndex(i), false, i, view, extent, isPointerOverProperties);
                    rowArea.y += ROW_HEIGHT;
                }
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    DrawLane(rowArea, tracks.GetArrayElementAtIndex(i), true, i, view, extent, isPointerOverProperties);
                    rowArea.y += ROW_HEIGHT;
                }
            }
            finally { GUI.EndScrollView(); }
        }

        private TimeView GetTimeView(float extent)
        {
            if (_viewDuration <= 0f) return new TimeView(0f, extent);
            float duration = Mathf.Clamp(_viewDuration, extent / MAX_ZOOM, extent);
            return new TimeView(Mathf.Clamp(_viewStart, 0f, extent - duration), duration);
        }

        private void FitTimeline()
        {
            _viewStart = 0f;
            _viewDuration = 0f;
            Repaint();
        }

        private void HandleViewInput(Rect lane, TimeView view, float extent, bool isPointerOverTracks)
        {
            var input = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive, lane);
            bool isPointerOverTime = isPointerOverTracks && lane.Contains(input.mousePosition);
            if (input.type == EventType.ScrollWheel && isPointerOverTime && input.delta.y != 0f)
            {
                if (GUIUtility.hotControl == 0)
                {
                    float fraction = Mathf.Clamp01((input.mousePosition.x - lane.x) / lane.width);
                    float delta = Mathf.Clamp(input.delta.y, -12f, 12f);
                    float duration = Mathf.Clamp(view.Duration * Mathf.Pow(WHEEL_ZOOM_FACTOR, delta), extent / MAX_ZOOM, extent);
                    // 锁定鼠标指向的时间，仅在视区碰到动画边界时限制起点。
                    _viewStart = Mathf.Clamp(view.Start + fraction * (view.Duration - duration), 0f, extent - duration);
                    _viewDuration = duration < extent ? duration : 0f;
                    Repaint();
                }
                input.Use();
                return;
            }

            if (GUIUtility.hotControl == control)
            {
                if (input.type == EventType.MouseDrag)
                {
                    _viewStart = Mathf.Clamp(view.Start - input.delta.x * view.Duration / lane.width, 0f, extent - view.Duration);
                    input.Use();
                    Repaint();
                }
                else if (input.type == EventType.MouseUp && input.button == 2)
                {
                    GUIUtility.hotControl = 0;
                    input.Use();
                }
                return;
            }

            if (!isPointerOverTime || GUIUtility.hotControl != 0) return;
            if (input.type == EventType.MouseDown && input.button == 2)
            {
                GUIUtility.hotControl = control;
                input.Use();
            }
            else if (input.type == EventType.KeyDown && input.keyCode == KeyCode.F &&
                     !input.alt && !input.control && !input.command && !EditorGUIUtility.editingTextField)
            {
                FitTimeline();
                input.Use();
            }
        }

        private void DrawRuler(Rect area, TimeView view, bool isPointerOverTracks)
        {
            Rect lane = Lane(area);
            float desiredStep = view.Duration / lane.width * MIN_TICK_SPACING;
            float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(desiredStep)));
            float units = desiredStep / power;
            float step = power * (units <= 1f ? 1f : units <= 2f ? 2f : units <= 5f ? 5f : 10f);
            double firstTick = Math.Ceiling(view.Start / (double)step) * step;
            int tickCount = Mathf.CeilToInt(lane.width / MIN_TICK_SPACING) + 1;
            for (int i = 0; i < tickCount; i++)
            {
                float seconds = (float)(firstTick + i * (double)step);
                if (seconds > view.End) break;
                float x = view.TimeToX(lane, seconds);
                EditorGUI.DrawRect(new Rect(x, lane.yMax - 5f, 1f, 4f), Color.gray);
                GUI.Label(new Rect(x + 2f, lane.y, Mathf.Max(0f, lane.xMax - x - 2f), lane.height),
                    $"{seconds:0.#####}s", EditorStyles.miniLabel);
            }
            DrawPlayhead(lane, view);

            var input = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive, lane);
            if (input.type == EventType.MouseDown && input.button == 0 && isPointerOverTracks && lane.Contains(input.mousePosition))
                GUIUtility.hotControl = control;
            if (GUIUtility.hotControl != control) return;
            if (input.type == EventType.MouseDown || input.type == EventType.MouseDrag)
            {
                _playing = false;
                PreviewAt(view.XToTime(lane, input.mousePosition.x));
                input.Use();
            }
            else if (input.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                input.Use();
            }
        }

        private void DrawEventLane(Rect area, SerializedProperty events, TimeView view, float extent,
            bool isPointerOverTracks)
        {
            Rect lane = Lane(area);
            var input = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive, area);
            bool isClick = isPointerOverTracks && input.type == EventType.MouseDown && input.button == 0 &&
                           GUIUtility.hotControl == 0 && area.Contains(input.mousePosition);
            EditorGUI.DrawRect(area, _isEventLaneSelected ? _selectedLaneColor : _laneColor);
            GUI.Label(new Rect(area.x + 6f, area.y, LABEL_WIDTH - 8f, area.height), _eventLaneContent);

            int hit = -1;
            float closest = float.PositiveInfinity;
            Rect selected = default;
            for (int i = 0; i < events.arraySize; i++)
            {
                bool isSelected = _isEventLaneSelected && i == _eventIndex;
                float time = isSelected && _eventDragControl == control
                    ? _eventDragTime : events.GetArrayElementAtIndex(i).FindPropertyRelative("_time").floatValue;
                if (time < view.Start || time > view.End || float.IsNaN(time)) continue;
                float x = view.TimeToX(lane, time);
                var marker = Rect.MinMaxRect(Mathf.Max(lane.xMin, x - 4f), lane.y + 4f,
                    Mathf.Min(lane.xMax, x + 4f), lane.yMax - 4f);
                if (isSelected) selected = marker;
                else EditorGUI.DrawRect(marker, _eventMarkerColor);
                EditorGUIUtility.AddCursorRect(marker, MouseCursor.ResizeHorizontal);

                // 同一位置优先保留当前选择。列表按序号区分名称和时间均相同的标记。
                float distance = Mathf.Abs(input.mousePosition.x - x);
                if (isClick && marker.Contains(input.mousePosition) &&
                    (distance < closest || (distance == closest && isSelected)))
                {
                    hit = i;
                    closest = distance;
                }
            }
            if (selected.width > 0f) EditorGUI.DrawRect(selected, _playheadColor);
            DrawPlayhead(lane, view);

            if (isClick)
            {
                SelectEvent(hit);
                if (hit >= 0)
                {
                    _eventDragTime = events.GetArrayElementAtIndex(hit).FindPropertyRelative("_time").floatValue;
                    _eventDragControl = control;
                    BeginDrag(control, _eventDragTime, extent, view.Duration / lane.width, isResizing: false);
                }
                else input.Use();
                Repaint();
            }
            HandleEventDrag(control, events);
        }

        private void HandleEventDrag(int control, SerializedProperty events)
        {
            if (_eventDragControl == 0) return;
            if (_eventDragControl != control || GUIUtility.hotControl != control ||
                _eventIndex < 0 || _eventIndex >= events.arraySize)
            {
                CancelEventDrag();
                return;
            }

            var input = Event.current;
            if (input.type == EventType.MouseDrag)
            {
                _eventDragTime = ReadDraggedTime(input.mousePosition.x);
                input.Use();
                Repaint();
            }
            else if (input.type == EventType.MouseUp && input.button == 0)
            {
                // 拖动只更新标记预览，松手时才改变序列化数据，整次手势只提交一次 Undo。
                var time = events.GetArrayElementAtIndex(_eventIndex).FindPropertyRelative("_time");
                if (time.floatValue != _eventDragTime) time.floatValue = _eventDragTime;
                CancelEventDrag();
                input.Use();
                Repaint();
            }
            else if (input.type == EventType.Ignore || input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            {
                CancelEventDrag();
                if (input.type == EventType.KeyDown) input.Use();
                Repaint();
            }
        }

        private void CancelEventDrag()
        {
            if (_eventDragControl == 0) return;
            if (GUIUtility.hotControl == _eventDragControl) GUIUtility.hotControl = 0;
            _eventDragControl = 0;
            _dragExtent = 0f;
        }

        private void DrawLane(Rect area, SerializedProperty row, bool isTrack, int index, TimeView view, float extent,
            bool isPointerOverTracks)
        {
            Rect lane = Lane(area);
            bool isSelected = !_isEventLaneSelected && _row == index && _track == isTrack;
            EditorGUI.DrawRect(area, isSelected ? _selectedLaneColor : _laneColor);
            GUI.Label(new Rect(area.x + 4f, area.y, LABEL_WIDTH - 8f, area.height), GetBinding(row).ToString(), EditorStyles.miniLabel);
            int control = GUIUtility.GetControlID(FocusType.Passive, lane);
            var input = Event.current;
            bool isLeftClick = input.type == EventType.MouseDown && input.button == 0 && isPointerOverTracks;

            if (isLeftClick && area.Contains(input.mousePosition))
            {
                SelectRow(isTrack, index);
                Repaint();
            }

            if (isTrack)
                DrawKeys(row.FindPropertyRelative("_keys"), lane, view, extent, control, isSelected, isLeftClick);
            else
                DrawClip(row, lane, view, extent, control, isLeftClick);
            DrawPlayhead(lane, view);
        }

        private void DrawKeys(SerializedProperty keys, Rect lane, TimeView view, float extent, int control,
            bool isSelected, bool isLeftClick)
        {
            var input = Event.current;
            for (int i = 0; i < keys.arraySize; i++)
            {
                var time = keys.GetArrayElementAtIndex(i).FindPropertyRelative("_time");
                float seconds = ValidSeconds(time.floatValue);
                if (seconds < view.Start || seconds > view.End) continue;
                float x = view.TimeToX(lane, seconds);
                var marker = Rect.MinMaxRect(Mathf.Max(x - 5f, lane.xMin), lane.y + 6f,
                    Mathf.Min(x + 5f, lane.xMax), lane.y + 17f);
                EditorGUI.DrawRect(marker, isSelected && i == _key ? new Color(1f, 0.78f, 0.38f) : new Color(0.56f, 0.73f, 1f));
                if (isLeftClick && input.type == EventType.MouseDown && marker.Contains(input.mousePosition))
                {
                    _key = i;
                    BeginDrag(control, seconds, extent, view.Duration / lane.width, false);
                }
            }
            if (_key >= 0 && _key < keys.arraySize && isSelected)
                HandleDrag(control, keys.GetArrayElementAtIndex(_key).FindPropertyRelative("_time"));
        }

        private void DrawClip(SerializedProperty row, Rect lane, TimeView view, float extent, int control, bool isLeftClick)
        {
            var start = row.FindPropertyRelative("_start");
            var duration = row.FindPropertyRelative("_duration");
            float startTime = ValidSeconds(start.floatValue);
            float durationTime = ValidSeconds(duration.floatValue);
            float endTime = startTime + durationTime;
            if (startTime <= view.End && endTime >= view.Start)
            {
                float startX = view.TimeToX(lane, startTime);
                float endX = view.TimeToX(lane, endTime);
                var bar = Rect.MinMaxRect(Mathf.Max(startX, lane.xMin), lane.y + 3f,
                    Mathf.Min(Mathf.Max(startX + 7f, endX), lane.xMax), lane.yMax - 3f);
                if (bar.width > 0f)
                {
                    EditorGUI.DrawRect(bar, new Color(0.24f, 0.55f, 0.81f));
                    // 只在实际终点可见时提供缩放手柄，裁剪边缘仍可拖动整个片段。
                    bool isEndVisible = endTime <= view.End;
                    var grip = Rect.MinMaxRect(Mathf.Max(bar.xMin, bar.xMax - 6f), bar.yMin, bar.xMax, bar.yMax);
                    if (isEndVisible)
                    {
                        EditorGUI.DrawRect(grip, new Color(0.55f, 0.75f, 0.93f));
                        EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeHorizontal);
                    }
                    if (isLeftClick && bar.Contains(Event.current.mousePosition))
                    {
                        bool isResize = isEndVisible && grip.Contains(Event.current.mousePosition);
                        BeginDrag(control, isResize ? durationTime : startTime, extent, view.Duration / lane.width, isResize);
                    }
                }
            }
            HandleDrag(control, _isResizing ? duration : start);
        }

        private void DrawPlayhead(Rect lane, TimeView view)
        {
            if (_seek < view.Start || _seek > view.End) return;
            float x = Mathf.Min(view.TimeToX(lane, _seek), lane.xMax - 1f);
            EditorGUI.DrawRect(new Rect(x, lane.y, 1f, lane.height), _playheadColor);
        }

        private void SelectRow(bool track, int index)
        {
            _isEventLaneSelected = false;
            _eventIndex = -1;
            if (_track == track && _row == index) return;
            _track = track;
            _row = index;
            _key = -1;
        }

        private void SelectEvent(int index)
        {
            _isEventLaneSelected = true;
            _eventIndex = index;
            _row = -1;
            _key = -1;
        }

        private void BeginDrag(int control, float time, float extent, float secondsPerPoint, bool isResizing)
        {
            GUIUtility.hotControl = control;
            _dragX = Event.current.mousePosition.x;
            _dragTime = time;
            _dragExtent = extent;
            _dragSecondsPerPoint = secondsPerPoint;
            // 放大后细化吸附步长，使短动画的拖动保持像素级反馈。
            float snapLimit = Mathf.Min(MAX_DRAG_SNAP, secondsPerPoint * DRAG_SNAP_SPACING);
            _dragSnapStep = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(snapLimit)));
            _isResizing = isResizing;
            Event.current.Use();
        }

        private void HandleDrag(int control, SerializedProperty time)
        {
            var input = Event.current;
            if (GUIUtility.hotControl != control) return;
            if (input.type == EventType.MouseDrag)
            {
                // 拖动开始时固定秒与像素的比例，避免时长或窗口尺寸变化导致时间跳动。
                time.floatValue = ReadDraggedTime(input.mousePosition.x);
                input.Use();
            }
            else if (input.type == EventType.MouseUp && input.button == 0)
            {
                GUIUtility.hotControl = 0;
                _dragExtent = 0f;
                input.Use();
            }
        }

        private float ReadDraggedTime(float mouseX)
        {
            float delta = (mouseX - _dragX) * _dragSecondsPerPoint;
            float snappedDelta = Mathf.Round(delta / _dragSnapStep) * _dragSnapStep;
            return ValidSeconds(_dragTime + snappedDelta);
        }

        private void DrawSelectedEvent(SerializedProperty events)
        {
            using var panel = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            if (events.arraySize == 0)
            {
                EditorGUILayout.LabelField("移动播放头后点击「游标处添加事件」。");
                return;
            }

            var options = new string[events.arraySize + 1];
            options[0] = "选择事件";
            for (int i = 0; i < events.arraySize; i++)
            {
                var marker = events.GetArrayElementAtIndex(i);
                float seconds = marker.FindPropertyRelative("_time").floatValue;
                string name = marker.FindPropertyRelative("_eventName").stringValue;
                options[i + 1] = $"{i + 1}. {name} · {seconds:0.#####} 秒";
            }
            int selection = _eventIndex >= 0 && _eventIndex < events.arraySize ? _eventIndex + 1 : 0;
            _eventIndex = EditorGUILayout.Popup("事件", selection, options) - 1;
            if (_eventIndex < 0) return;
            var row = events.GetArrayElementAtIndex(_eventIndex);
            var nameProperty = row.FindPropertyRelative("_eventName");
            string enteredName = EditorGUILayout.DelayedTextField("事件名称", nameProperty.stringValue);
            if (enteredName != nameProperty.stringValue) nameProperty.stringValue = enteredName.Trim();
            var timeProperty = row.FindPropertyRelative("_time");
            float enteredTime = EditorGUILayout.DelayedFloatField("时间（秒）", timeProperty.floatValue);
            if (enteredTime != timeProperty.floatValue) timeProperty.floatValue = ValidSeconds(enteredTime);
            if (GUILayout.Button("删除事件"))
            {
                events.DeleteArrayElementAtIndex(_eventIndex);
                _eventIndex = -1;
            }
        }

        private void DrawSelectedRow(SerializedProperty clips, SerializedProperty tracks)
        {
            var rows = _track ? tracks : clips;
            if (_row < 0 || _row >= rows.arraySize) return;
            var row = rows.GetArrayElementAtIndex(_row);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(_track ? "关键帧轨道" : "片段", EditorStyles.boldLabel);
                    if (GUILayout.Button("删除", GUILayout.Width(56f)))
                    {
                        rows.DeleteArrayElementAtIndex(_row);
                        _row = -1;
                        return;
                    }
                }

                var property = row.FindPropertyRelative("_property");
                var target = row.FindPropertyRelative("_target");
                EditorGUI.BeginChangeCheck();
                var kind = (UITimelineProperty)EditorGUILayout.EnumPopup("属性", (UITimelineProperty)property.enumValueIndex);
                bool hasKindChanged = EditorGUI.EndChangeCheck();
                if (hasKindChanged) property.enumValueIndex = (int)kind;
                Type targetType = UITimelineBinding.TargetType(kind);
                var component = target.objectReferenceValue as Component;
                if (component != null && !targetType.IsInstanceOfType(component))
                    component = component.GetComponent(targetType);
                EditorGUI.BeginChangeCheck();
                var selectedTarget = EditorGUILayout.ObjectField("目标", component, targetType, true);
                if (EditorGUI.EndChangeCheck() || hasKindChanged) target.objectReferenceValue = selectedTarget;
                var binding = GetBinding(row);

                if (_track)
                {
                    if (GUILayout.Button("在游标处记录关键帧")) RecordKey(row, _seek);
                    var keys = row.FindPropertyRelative("_keys");
                    if (_key < 0 || _key >= keys.arraySize) return;
                    var selected = keys.GetArrayElementAtIndex(_key);
                    EditTime(selected, "_time", "时间");
                    DrawValue(selected.FindPropertyRelative("_value"), binding, "值");
                    EditorGUILayout.PropertyField(selected.FindPropertyRelative("_ease"), new GUIContent("到下一帧的缓动"));
                    if (GUILayout.Button("删除关键帧"))
                    {
                        keys.DeleteArrayElementAtIndex(_key);
                        _key = -1;
                    }
                }
                else
                {
                    EditTime(row, "_start", "开始");
                    EditTime(row, "_duration", "时长");
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("_ease"), new GUIContent("缓动"));
                    DrawValue(row.FindPropertyRelative("_from"), binding, "起点");
                    DrawValue(row.FindPropertyRelative("_to"), binding, "终点");
                }
            }
        }

        private void SetDefaultBinding(SerializedProperty row)
        {
            UIBindingRoot bindingRoot = _timeline.GetComponentInParent<UIBindingRoot>(true);
            Transform root = bindingRoot == null ? _timeline.transform : bindingRoot.transform;
            var target = Selection.activeTransform as RectTransform;
            if (target == null || !target.IsChildOf(root)) target = _timeline.transform as RectTransform;
            row.FindPropertyRelative("_property").enumValueIndex = (int)UITimelineProperty.AnchoredPosition;
            row.FindPropertyRelative("_target").objectReferenceValue = target;
        }

        private void RecordKey(SerializedProperty row, float time)
        {
            var keys = row.FindPropertyRelative("_keys");
            // 放大时保留游标的精细时间，避免按全览精度取整后插到视区之外。
            float keyTime = _viewDuration > 0f ? ValidSeconds(time) : Snap(time);
            _key = 0;
            for (; _key < keys.arraySize; _key++)
                if (Mathf.Approximately(keyTime, keys.GetArrayElementAtIndex(_key).FindPropertyRelative("_time").floatValue))
                    break;
            bool newKey = _key == keys.arraySize;
            var key = newKey ? Append(keys) : keys.GetArrayElementAtIndex(_key);
            var binding = GetBinding(row);
            key.FindPropertyRelative("_time").floatValue = keyTime;
            key.FindPropertyRelative("_value").vector4Value = binding.Target == null ? Vector4.zero : binding.Read();
            if (newKey) key.FindPropertyRelative("_ease").enumValueIndex = 0;
        }

        private static void DrawValue(SerializedProperty value, UITimelineBinding binding, string label)
        {
            using var line = new EditorGUILayout.HorizontalScope();
            Vector4 current = value.vector4Value;
            EditorGUI.BeginChangeCheck();
            Vector4 entered = binding.Property switch
            {
                UITimelineProperty.Color => (Vector4)EditorGUILayout.ColorField(label, current),
                UITimelineProperty.AnchoredPosition => EditorGUILayout.Vector2Field(label, current),
                UITimelineProperty.Scale => EditorGUILayout.Vector3Field(label, current),
                UITimelineProperty.Rotation => new Vector4(EditorGUILayout.FloatField(label, current.x), 0f, 0f, 0f),
                _ => new Vector4(EditorGUILayout.Slider(label, current.x, 0f, 1f), 0f, 0f, 0f),
            };
            if (EditorGUI.EndChangeCheck()) value.vector4Value = entered;
            using var button = new EditorGUI.DisabledScope(binding.Target == null);
            if (GUILayout.Button("捕获当前", GUILayout.Width(78f))) value.vector4Value = binding.Read();
        }

        private void DrawPreviewControls()
        {
            var data = _timeline.Animations[_animation];
            float duration = Length(data);
            EditorGUI.BeginChangeCheck();
            float seconds;
            using (new EditorGUILayout.HorizontalScope())
            {
                seconds = EditorGUILayout.Slider("预览进度", _seek, 0f, Mathf.Max(1f, duration, _seek));
                seconds = EditorGUILayout.DelayedFloatField(seconds, GUILayout.Width(72f));
            }
            if (EditorGUI.EndChangeCheck())
            {
                _playing = false;
                PreviewAt(seconds);
            }

            using var line = new EditorGUILayout.HorizontalScope();
            if (GUILayout.Button(_playing ? "暂停" : "播放预览"))
            {
                if (_playing) _playing = false;
                else
                {
                    PreviewAt(_seek >= duration ? 0f : _seek);
                    _playing = _preview != null && _preview.Duration > 0f;
                    _lastTick = EditorApplication.timeSinceStartup;
                }
            }
            if (GUILayout.Button("停止并恢复")) StopPreview();
            GUILayout.Label($"{duration:0.###} 秒", EditorStyles.miniLabel);
        }

        private void PreviewAt(float seconds)
        {
            try
            {
                if (_preview == null)
                {
                    if (_activeWindow != null && _activeWindow != this) _activeWindow.StopPreview();
                    var playback = _timeline.CreatePlayback(_timeline.Animations[_animation]);
                    playback.Capture();
                    _preview = playback;
                    _activeWindow = this;
                }
                _seek = ValidSeconds(seconds);
                _preview.Seek(Mathf.Min(_seek, _preview.Duration));
                _error = null;
            }
            catch (InvalidOperationException error)
            {
                StopPreview();
                _error = error.Message;
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void RebuildPreview()
        {
            float seconds = _seek;
            bool playing = _playing;
            StopPreview();
            PreviewAt(seconds);
            _playing = playing && _preview != null;
        }

        private void TickPreview()
        {
            if (_preview == null) return;
            if (_timeline == null) { StopPreview(); return; }
            if (!_playing) return;
            double now = EditorApplication.timeSinceStartup;
            float seconds = Mathf.Min(_preview.Duration, _seek + (float)(now - _lastTick));
            _lastTick = now;
            PreviewAt(seconds);
            if (_preview == null || _seek >= _preview.Duration) _playing = false;
        }

        private void StopPreview()
        {
            _playing = false;
            if (_preview != null)
            {
                _preview.Restore();
                _preview.Kill();
                _preview = null;
                SceneView.RepaintAll();
            }
            if (_activeWindow == this) _activeWindow = null;
            Repaint();
        }

        private void OnPrefabSaving(GameObject root)
        {
            if (_timeline != null && root != null && _timeline.transform.IsChildOf(root.transform)) StopPreview();
        }

        private void OnSceneSaving(Scene scene, string path)
        {
            if (_timeline != null && _timeline.gameObject.scene == scene) StopPreview();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) StopPreview();
        }

        private static UITimelineBinding GetBinding(SerializedProperty row) => new(
            row.FindPropertyRelative("_target").objectReferenceValue as Component,
            (UITimelineProperty)row.FindPropertyRelative("_property").enumValueIndex);

        private static SerializedProperty Append(SerializedProperty list)
        {
            int index = list.arraySize;
            list.arraySize++;
            return list.GetArrayElementAtIndex(index);
        }

        private static Rect Lane(Rect area) => new(area.x + LABEL_WIDTH, area.y,
            Mathf.Max(1f, area.width - LABEL_WIDTH - 8f), area.height);
        private static float ValidSeconds(float time) => time > 0f && !float.IsInfinity(time) ? time : 0f;
        private static float Snap(float time) => ValidSeconds(Mathf.Round(time * 100f) / 100f);
        private static void SetDuration(SerializedProperty item, string field, float seconds) =>
            item.FindPropertyRelative(field).floatValue = ValidSeconds(seconds);
        private static void EditTime(SerializedProperty item, string field, string label)
        {
            var property = item.FindPropertyRelative(field);
            EditorGUI.BeginChangeCheck();
            float entered = EditorGUILayout.FloatField(label, property.floatValue);
            if (EditorGUI.EndChangeCheck()) property.floatValue = ValidSeconds(entered);
        }

        private static float Length(SerializedProperty clips, SerializedProperty tracks, SerializedProperty events)
        {
            float duration = 0f;
            for (int i = 0; i < clips.arraySize; i++)
            {
                var clip = clips.GetArrayElementAtIndex(i);
                duration = Mathf.Max(duration, clip.FindPropertyRelative("_start").floatValue + clip.FindPropertyRelative("_duration").floatValue);
            }
            for (int i = 0; i < tracks.arraySize; i++)
            {
                var keys = tracks.GetArrayElementAtIndex(i).FindPropertyRelative("_keys");
                for (int j = 0; j < keys.arraySize; j++)
                    duration = Mathf.Max(duration, keys.GetArrayElementAtIndex(j).FindPropertyRelative("_time").floatValue);
            }
            for (int i = 0; i < events.arraySize; i++)
                duration = Mathf.Max(duration, ValidSeconds(events.GetArrayElementAtIndex(i).FindPropertyRelative("_time").floatValue));
            return duration;
        }

        private static float Length(UITimelineAnimation animation)
        {
            float duration = 0f;
            foreach (var clip in animation.Clips)
                if (clip != null) duration = Mathf.Max(duration, clip.Span.End);
            foreach (var track in animation.Tracks)
                if (track != null)
                    foreach (var key in track.Keys)
                        if (key != null) duration = Mathf.Max(duration, key.Time);
            foreach (var marker in animation.Events)
                if (marker != null) duration = Mathf.Max(duration, ValidSeconds(marker.Time));
            return duration;
        }

        #endregion
    }

    /// <summary>显示时间轴动画数量并提供打开工作台的入口。</summary>
    [CustomEditor(typeof(UITimeline))]
    internal sealed class UITimelineInspector : UnityEditor.Editor
    {
        /// <summary>绘制动画数量和时间轴工作台入口。</summary>
        public override void OnInspectorGUI()
        {
            var timeline = (UITimeline)target;
            EditorGUILayout.LabelField("已配置动画", timeline.Animations.Count.ToString());
            if (GUILayout.Button("打开时间轴工作台")) UITimelineWorkbench.Open(timeline);
        }
    }
}
