using System;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework.Editor.UI
{
    internal sealed class UITimelineWorkbench : EditorWindow
    {
        private const float LabelWidth = 156f;
        private static UITimelineWorkbench _activeWindow;

        private UITimeline _timeline;
        private SerializedObject _serialized;
        private UITimelinePlayback _preview;
        private Vector2 _windowScroll;
        private Vector2 _scroll;
        private int _animation;
        private int _row;
        private int _key;
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
        private bool _resize;

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
            Undo.undoRedoPerformed += StopPreview;
        }

        private void OnDisable()
        {
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= TickPreview;
            Undo.undoRedoPerformed -= StopPreview;
            StopPreview();
            _serialized?.Dispose();
            _serialized = null;
        }

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
            _seek = 0f;
            _error = null;
            _validate = true;
        }

        private void OnGUI()
        {
            var timeline = (UITimeline)EditorGUILayout.ObjectField("时间轴", _timeline, typeof(UITimeline), true);
            if (timeline != _timeline) SetTimeline(timeline);
            if (timeline == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("在编辑模式选择带 UITimeline 的节点。", MessageType.Info);
                return;
            }
            _serialized ??= new SerializedObject(timeline);
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
            EditorGUILayout.PropertyField(_serialized.FindProperty("_timeMode"), new GUIContent("计时方式"));
            DrawTracks(clips, tracks);
            DrawSelectedRow(clips, tracks);
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
                    SelectAnimation(index);
                }
            }

            if (animations.arraySize == 0) return;
            var animationName = animations.GetArrayElementAtIndex(_animation).FindPropertyRelative("_name");
            using (new EditorGUILayout.HorizontalScope())
            {
                string name = EditorGUILayout.DelayedTextField("名称", animationName.stringValue).Trim();
                if (name != animationName.stringValue)
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

        private void DrawTracks(SerializedProperty clips, SerializedProperty tracks)
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
            }

            float extent = GUIUtility.hotControl != 0 && _dragExtent > 0f
                ? _dragExtent : Mathf.Max(1f, 1.12f * Length(clips, tracks), _seek);
            using (var view = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.MinHeight(146f), GUILayout.MaxHeight(260f)))
            {
                _scroll = view.scrollPosition;
                DrawRuler(extent);
                for (int i = 0; i < clips.arraySize; i++)
                    DrawLane(clips.GetArrayElementAtIndex(i), false, i, extent);
                for (int i = 0; i < tracks.arraySize; i++)
                    DrawLane(tracks.GetArrayElementAtIndex(i), true, i, extent);
            }
        }

        private void DrawRuler(float extent)
        {
            Rect area = GUILayoutUtility.GetRect(110f, 24f, GUILayout.ExpandWidth(true));
            Rect lane = Lane(area);
            for (int i = 0; i < 5; i++)
            {
                float fraction = i / 4f;
                float x = lane.x + lane.width * fraction;
                EditorGUI.DrawRect(new Rect(x, lane.yMax - 5f, 1f, 4f), Color.gray);
                GUI.Label(new Rect(Mathf.Min(x + 2f, lane.xMax - 32f), lane.y, 34f, lane.height),
                    $"{(extent * fraction):0.##}s", EditorStyles.miniLabel);
            }

            var input = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive, lane);
            if (input.type == EventType.MouseDown && input.button == 0 && lane.Contains(input.mousePosition))
                GUIUtility.hotControl = control;
            if (GUIUtility.hotControl != control) return;
            if (input.type == EventType.MouseDown || input.type == EventType.MouseDrag)
            {
                _playing = false;
                PreviewAt(Mathf.Clamp01((input.mousePosition.x - lane.x) / lane.width) * extent);
                input.Use();
            }
            else if (input.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                input.Use();
            }
        }

        private void DrawLane(SerializedProperty row, bool track, int index, float extent)
        {
            Rect area = GUILayoutUtility.GetRect(110f, 23f, GUILayout.ExpandWidth(true));
            Rect lane = Lane(area);
            bool selected = _row == index && _track == track;
            EditorGUI.DrawRect(area, selected ? new Color(0.26f, 0.32f, 0.4f) : new Color(0.15f, 0.18f, 0.21f));
            GUI.Label(new Rect(area.x + 4f, area.y, LabelWidth - 8f, area.height), GetBinding(row).ToString(), EditorStyles.miniLabel);
            int control = GUIUtility.GetControlID(FocusType.Passive, lane);
            var input = Event.current;

            if (input.type == EventType.MouseDown && input.button == 0 && area.Contains(input.mousePosition))
            {
                SelectRow(track, index);
                Repaint();
            }

            if (track)
            {
                var keys = row.FindPropertyRelative("_keys");
                for (int i = 0; i < keys.arraySize; i++)
                {
                    var time = keys.GetArrayElementAtIndex(i).FindPropertyRelative("_time");
                    float x = TimeX(lane, time.floatValue, extent);
                    var marker = new Rect(x - 5f, lane.y + 6f, 10f, 11f);
                    EditorGUI.DrawRect(marker, selected && i == _key ? new Color(1f, 0.78f, 0.38f) : new Color(0.56f, 0.73f, 1f));
                    if (input.type == EventType.MouseDown && input.button == 0 && marker.Contains(input.mousePosition))
                    {
                        _key = i;
                        BeginDrag(control, time.floatValue, extent, false);
                    }
                }
                if (_key >= 0 && _key < keys.arraySize && selected)
                    HandleDrag(control, lane, keys.GetArrayElementAtIndex(_key).FindPropertyRelative("_time"));
            }
            else
            {
                var start = row.FindPropertyRelative("_start");
                var duration = row.FindPropertyRelative("_duration");
                float x = TimeX(lane, start.floatValue, extent);
                float width = Mathf.Max(7f, duration.floatValue * lane.width / extent);
                var bar = new Rect(x, lane.y + 3f, width, lane.height - 6f);
                var grip = new Rect(bar.xMax - 6f, bar.y, 6f, bar.height);
                EditorGUI.DrawRect(bar, new Color(0.24f, 0.55f, 0.81f));
                EditorGUI.DrawRect(grip, new Color(0.55f, 0.75f, 0.93f));
                EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeHorizontal);
                if (input.type == EventType.MouseDown && input.button == 0 && bar.Contains(input.mousePosition))
                {
                    bool resize = grip.Contains(input.mousePosition);
                    BeginDrag(control, resize ? duration.floatValue : start.floatValue, extent, resize);
                }
                HandleDrag(control, lane, _resize ? duration : start);
            }

            float cursor = TimeX(lane, _seek, extent);
            EditorGUI.DrawRect(new Rect(cursor, lane.y, 1f, lane.height), new Color(0.96f, 0.8f, 0.32f));
        }

        private void SelectRow(bool track, int index)
        {
            if (_track == track && _row == index) return;
            _track = track;
            _row = index;
            _key = -1;
        }

        private void BeginDrag(int control, float time, float extent, bool resize)
        {
            GUIUtility.hotControl = control;
            _dragX = Event.current.mousePosition.x;
            _dragTime = time;
            _dragExtent = extent;
            _resize = resize;
            Event.current.Use();
        }

        private void HandleDrag(int control, Rect lane, SerializedProperty time)
        {
            var input = Event.current;
            if (GUIUtility.hotControl != control) return;
            if (input.type == EventType.MouseDrag)
            {
                time.floatValue = Snap(_dragTime + (input.mousePosition.x - _dragX) * _dragExtent / lane.width);
                input.Use();
            }
            else if (input.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                _dragExtent = 0f;
                input.Use();
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
                var kind = (UITimelineProperty)property.enumValueIndex;
                kind = (UITimelineProperty)EditorGUILayout.EnumPopup("属性", kind);
                property.enumValueIndex = (int)kind;
                Type targetType = UITimelineBinding.TargetType(kind);
                var component = target.objectReferenceValue as Component;
                if (component != null && !targetType.IsInstanceOfType(component))
                    component = component.GetComponent(targetType);
                target.objectReferenceValue = EditorGUILayout.ObjectField("目标", component, targetType, true);
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
            float snapped = Snap(time);
            _key = 0;
            for (; _key < keys.arraySize; _key++)
                if (Mathf.Approximately(snapped, keys.GetArrayElementAtIndex(_key).FindPropertyRelative("_time").floatValue))
                    break;
            bool newKey = _key == keys.arraySize;
            var key = newKey ? Append(keys) : keys.GetArrayElementAtIndex(_key);
            var binding = GetBinding(row);
            key.FindPropertyRelative("_time").floatValue = snapped;
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

        private static Rect Lane(Rect area) => new(area.x + LabelWidth, area.y, Mathf.Max(1f, area.width - LabelWidth - 8f), area.height);
        private static float TimeX(Rect lane, float seconds, float extent) => lane.x + lane.width * Mathf.Clamp01(seconds / extent);
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

        private static float Length(SerializedProperty clips, SerializedProperty tracks)
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
            return duration;
        }
    }

    [CustomEditor(typeof(UITimeline))]
    internal sealed class UITimelineInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var timeline = (UITimeline)target;
            EditorGUILayout.LabelField("已配置动画", timeline.Animations.Count.ToString());
            if (GUILayout.Button("打开时间轴工作台")) UITimelineWorkbench.Open(timeline);
        }
    }
}
