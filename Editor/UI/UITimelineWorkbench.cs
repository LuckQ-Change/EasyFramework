using System.Collections.Generic;
using EasyFramework.Tweening;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    /// <summary>在真实 Prefab 上拖动 Tween 时间轴，并在退出或保存前恢复原始画面。</summary>
    internal sealed class UITimelineWorkbench : EditorWindow
    {
        private sealed class OriginalValue
        {
            public Component Target;
            public UITimelineProperty Property;
            public Color Color;
            public Vector2 Position;
            public Vector3 Scale;
            public float Alpha;
        }

        private static UITimelineWorkbench _activeWindow;
        private readonly List<OriginalValue> _original = new List<OriginalValue>();
        private UITimeline _timeline;
        private TweenSequence _preview;
        private UnityEditor.Editor _inspector;
        private float _seek;

        [MenuItem("Tools/EasyFramework/UI/Tween Timeline", false, 21)]
        private static void OpenMenu() => Open(Selection.activeGameObject == null
            ? null : Selection.activeGameObject.GetComponentInParent<UITimeline>());

        internal static void Open(UITimeline timeline)
        {
            var window = GetWindow<UITimelineWorkbench>("UI Timeline");
            window.minSize = new Vector2(440f, 320f);
            window.SetTimeline(timeline);
            window.Show();
        }

        private void OnEnable()
        {
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += StopPreview;
        }

        private void OnDisable()
        {
            PrefabStage.prefabSaving -= OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= StopPreview;
            StopPreview();
            if (_inspector != null) DestroyImmediate(_inspector);
            _inspector = null;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) StopPreview();
        }

        private void SetTimeline(UITimeline timeline)
        {
            if (_timeline == timeline) return;
            StopPreview();
            if (_inspector != null) DestroyImmediate(_inspector);
            _inspector = null;
            _timeline = timeline;
            _seek = 0f;
            Repaint();
        }

        private void OnGUI()
        {
            UITimeline selected = (UITimeline)EditorGUILayout.ObjectField(
                "时间轴", _timeline, typeof(UITimeline), true);
            if (selected != _timeline) SetTimeline(selected);
            if (_timeline == null)
            {
                EditorGUILayout.HelpBox("选择带 UITimeline 的 UI 节点。", MessageType.Info);
                return;
            }
            if (_inspector == null) _inspector = UnityEditor.Editor.CreateEditor(_timeline);
            _inspector.OnInspectorGUI();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"时长 {_timeline.Duration:0.###} s", EditorStyles.boldLabel);
            DrawTracks();
            float duration = Mathf.Max(0.001f, _timeline.Duration);
            float next = EditorGUILayout.Slider("进度", _seek, 0f, duration);
            if (!Mathf.Approximately(next, _seek)) PreviewAt(next);
            EditorGUILayout.BeginHorizontal();
            if (_preview == null && GUILayout.Button("开始预览")) PreviewAt(_seek);
            if (_preview != null && GUILayout.Button("退出预览并恢复")) StopPreview();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("拖动进度使用运行时 TweenSequence.Seek。退出、撤销、切换播放模式或保存时会恢复原外观。",
                MessageType.None);
        }

        private void DrawTracks()
        {
            Rect area = GUILayoutUtility.GetRect(200f, Mathf.Max(36f, _timeline.Clips.Count * 25f + 8f));
            EditorGUI.DrawRect(area, new Color(0.12f, 0.14f, 0.18f));
            float duration = Mathf.Max(0.001f, _timeline.Duration);
            for (int i = 0; i < _timeline.Clips.Count; i++)
            {
                UITimelineClip clip = _timeline.Clips[i];
                if (clip == null) continue;
                float x = area.x + area.width * clip.Start / duration;
                float width = Mathf.Max(3f, area.width * clip.Duration / duration);
                var bar = new Rect(x, area.y + 5f + i * 25f, width, 20f);
                EditorGUI.DrawRect(bar, new Color(0.22f, 0.54f, 0.78f));
                GUI.Label(bar, $"{clip.Target?.name ?? "未绑定"} · {clip.Property}", EditorStyles.whiteMiniLabel);
            }
            float cursor = area.x + area.width * _seek / duration;
            EditorGUI.DrawRect(new Rect(cursor, area.y, 2f, area.height), Color.yellow);
        }

        private void PreviewAt(float seconds)
        {
            if (_preview == null)
            {
                if (_activeWindow != null && _activeWindow != this) _activeWindow.StopPreview();
                CacheOriginalValues();
                try { _preview = _timeline.BuildSequence(); }
                catch (System.Exception exception)
                {
                    RestoreOriginalValues();
                    Debug.LogException(exception, _timeline);
                    return;
                }
                _activeWindow = this;
            }
            _seek = Mathf.Clamp(seconds, 0f, _timeline.Duration);
            _preview.Seek(_seek);
            SceneView.RepaintAll();
            Repaint();
        }

        private void CacheOriginalValues()
        {
            _original.Clear();
            foreach (UITimelineClip clip in _timeline.Clips)
            {
                if (clip == null || clip.Target == null) continue;
                if (_original.Exists(item => item.Target == clip.Target && item.Property == clip.Property)) continue;
                var value = new OriginalValue { Target = clip.Target, Property = clip.Property };
                if (clip.Property == UITimelineProperty.Color && clip.Target is Graphic graphic)
                    value.Color = graphic.color;
                else if (clip.Property == UITimelineProperty.AnchoredPosition && clip.Target is RectTransform rect)
                    value.Position = rect.anchoredPosition;
                else if (clip.Property == UITimelineProperty.Scale && clip.Target is RectTransform scaleRect)
                    value.Scale = scaleRect.localScale;
                else if (clip.Property == UITimelineProperty.Alpha && clip.Target is CanvasGroup group)
                    value.Alpha = group.alpha;
                else
                    continue; // Invalid clips are reported by BuildSequence; never restore an uncaptured value.
                _original.Add(value);
            }
        }

        private void RestoreOriginalValues()
        {
            foreach (OriginalValue item in _original)
            {
                if (item.Target == null) continue;
                if (item.Property == UITimelineProperty.Color && item.Target is Graphic graphic)
                    graphic.color = item.Color;
                else if (item.Property == UITimelineProperty.AnchoredPosition && item.Target is RectTransform rect)
                    rect.anchoredPosition = item.Position;
                else if (item.Property == UITimelineProperty.Scale && item.Target is RectTransform scaleRect)
                    scaleRect.localScale = item.Scale;
                else if (item.Property == UITimelineProperty.Alpha && item.Target is CanvasGroup group)
                    group.alpha = item.Alpha;
            }
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            if (_preview == null) return;
            RestoreOriginalValues();
            _preview.Kill();
            _preview = null;
            _original.Clear();
            if (_activeWindow == this) _activeWindow = null;
            Repaint();
        }

        private void OnPrefabSaving(GameObject root)
        {
            if (_preview == null || _timeline == null || root == null ||
                !_timeline.transform.IsChildOf(root.transform)) return;
            RestoreForSave();
        }

        private void OnSceneSaving(Scene scene, string path)
        {
            if (_preview == null || _timeline == null || _timeline.gameObject.scene != scene) return;
            RestoreForSave();
        }

        private void RestoreForSave()
        {
            RestoreOriginalValues();
            EditorApplication.delayCall += () =>
            {
                if (_preview != null && _timeline != null) _preview.Seek(_seek);
                SceneView.RepaintAll();
            };
        }
    }
}
