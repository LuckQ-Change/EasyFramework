using System;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(UIVirtualList))]
    internal sealed class UIVirtualListInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Draw("_scrollRect", "滚动区域");
            Draw("_templates", "Item 模板");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("布局", EditorStyles.boldLabel);
            SerializedProperty layout = serializedObject.FindProperty("_layout");
            EditorGUILayout.PropertyField(layout, new GUIContent("排列"));
            Draw("_padding", "四边内边距");
            Draw("_spacing", "行 / 项间距");
            Draw("_indent", "层级缩进");
            if (layout.enumValueIndex == (int)UIVirtualListLayout.Grid)
            {
                Draw("_columns", "列数");
                Draw("_columnSpacing", "列间距");
            }
            Draw("_overscan", "额外保留行数", "在视口前后多保留的行数，用于减少快速滚动时的空白。");

            var list = (UIVirtualList)target;
            if (serializedObject.ApplyModifiedProperties()) UIVirtualListPreview.RefreshFor(list);
            try { list.ValidateConfiguration(); }
            catch (InvalidOperationException exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }

            if (GUILayout.Button("预览虚拟列表")) UIVirtualListPreview.Open(list);
        }

        private void Draw(string name, string label, string tooltip = null) =>
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label, tooltip), true);
    }

    internal sealed class UIVirtualListPreview : EditorWindow
    {
        [SerializeField] private UIVirtualList _source;

        private PreviewRenderUtility _preview;
        private UIVirtualList _list;
        private Vector2 _size;
        private string _error;
        private int _index;
        private bool _dirty;
        private bool _expanded = true;

        internal static void Open(UIVirtualList source)
        {
            var window = GetWindow<UIVirtualListPreview>("虚拟列表预览");
            window._source = source;
            window.minSize = new Vector2(400f, 350f);
            window.Invalidate();
        }

        internal static void RefreshFor(UIVirtualList source)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<UIVirtualListPreview>())
                if (window._source == source) window.Invalidate();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _dirty = true;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Invalidate;
            Undo.undoRedoPerformed -= Invalidate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Cleanup();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Cleanup();
            Invalidate();
        }

        private void Invalidate()
        {
            _dirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            var source = (UIVirtualList)EditorGUILayout.ObjectField("列表", _source, typeof(UIVirtualList), true);
            if (source != _source)
            {
                _source = source;
                _index = 0;
                Invalidate();
            }
            if (_source == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Cleanup();
                EditorGUILayout.HelpBox("在编辑模式下选择一个列表查看排版。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新模板")) Invalidate();
                bool expanded = GUILayout.Toggle(_expanded, "展开示例分组", EditorStyles.miniButton);
                if (expanded != _expanded)
                {
                    _expanded = expanded;
                    _index = 0;
                    Invalidate();
                }
            }

            if (_dirty && Event.current.type == EventType.Layout)
            {
                try { BuildPreview(_source); }
                catch (Exception exception)
                {
                    Cleanup();
                    _error = exception.GetBaseException().Message;
                }
                _dirty = false;
            }
            if (_list == null)
            {
                if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"{_list.Layout} · {_list.VisibleRowCount} 个示例项",
                "使用实际模板，显示示例数据。");
            int last = Mathf.Max(0, _list.VisibleRowCount - 1);
            int index = EditorGUILayout.IntSlider("滚动位置", Mathf.Min(_index, last), 0, last);
            Rect area = GUILayoutUtility.GetRect(1f, 10000f, 150f, 10000f);
            Event current = Event.current;
            if (current.type == EventType.ScrollWheel && area.Contains(current.mousePosition))
            {
                index = Mathf.Clamp(_index + (current.delta.y > 0f ? 2 : -2), 0, last);
                current.Use();
            }
            if (index != _index)
            {
                _index = index;
                _list.ScrollTo(index);
                Repaint();
            }
            if (current.type != EventType.Repaint || area.width <= 0f || area.height <= 0f) return;

            float scale = Mathf.Min(area.width / _size.x, area.height / _size.y);
            Rect frame = new Rect(area.center - _size * scale * 0.5f, _size * scale);
            Camera camera = _preview.camera;
            camera.orthographicSize = _size.y * 0.5f;
            _preview.BeginPreview(frame, GUIStyle.none);
            Texture result;
            try
            {
                Canvas.ForceUpdateCanvases();
                _preview.Render(updatefov: false);
            }
            finally { result = _preview.EndPreview(); }
            GUI.DrawTexture(frame, result, ScaleMode.StretchToFill, false);
        }

        internal void BuildPreview(UIVirtualList source)
        {
            Cleanup();
            _error = null;
            string[] keys = source.PreviewTemplateKeys.ToArray();

            RectTransform sourceRect = (RectTransform)source.transform;
            _size = sourceRect.rect.size;
            if (_size.x < 1f || _size.y < 1f)
                _size = source.Layout == UIVirtualListLayout.Horizontal ? new Vector2(520f, 220f) : new Vector2(320f, 440f);

            _preview = new PreviewRenderUtility();
            try
            {
                Camera camera = _preview.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1f);
                camera.orthographic = true;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                GameObject root = EditorUtility.CreateGameObjectWithHideFlags(
                    "List Preview Canvas", HideFlags.HideAndDontSave, typeof(RectTransform), typeof(Canvas));
                root.SetActive(false);
                _preview.AddSingleGO(root);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                ((RectTransform)root.transform).sizeDelta = _size;

                GameObject pool = EditorUtility.CreateGameObjectWithHideFlags(
                    "List Preview Pool", HideFlags.HideAndDontSave, typeof(RectTransform));
                pool.SetActive(false);
                _preview.AddSingleGO(pool);

                var clone = Object.Instantiate(source, root.transform, false);
                var rect = (RectTransform)clone.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition3D = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.sizeDelta = _size;
                if (!clone.Viewport.IsChildOf(clone.transform) || !clone.Content.IsChildOf(clone.Viewport))
                    throw new InvalidOperationException("预览需要列表内的 Scroll Rect、Viewport 与 Content。");

                KeepVisualComponents(rect, clone);
                clone.EnableEditorPreview((RectTransform)pool.transform, item => KeepVisualComponents(item, null));
                clone.gameObject.SetActive(true);
                root.SetActive(true);
                Canvas.ForceUpdateCanvases();

                var groups = Enumerable.Range(0, 4).Select(group =>
                {
                    var items = Enumerable.Range(0, Mathf.Max(6, keys.Length))
                        .Select(row => new UIVirtualListRow<string>($"{group}/{row}", keys[row % keys.Length],
                            $"示例项 {group + 1}.{row + 1}")).ToArray();
                    return new UIVirtualListRow<string>($"group-{group}", keys[0],
                        $"分组 {group + 1}", items, _expanded);
                }).ToArray();
                clone.SetItems(groups, BindSample);
                clone.ScrollTo(_index);
                _list = clone;
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        private static void KeepVisualComponents(RectTransform root, UIVirtualList list)
        {
            foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null || script == list || script is Graphic || script is Mask ||
                    script is RectMask2D || script is ScrollRect || script is LayoutGroup ||
                    script is ContentSizeFitter || script is AspectRatioFitter ||
                    script is LayoutElement || script is BaseMeshEffect) continue;
                Object.DestroyImmediate(script);
            }
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.overrideSorting = false;
                canvas.renderMode = RenderMode.WorldSpace;
            }
        }

        private static void BindSample(RectTransform item, string label, UIVirtualListItemContext context)
        {
            if (context.HasChildren) label = (context.IsExpanded ? "− " : "+ ") + label;
            foreach (Component component in item.GetComponentsInChildren<Component>(true))
                if (UIStateControlAccess.TrySetText(component, label)) break;
        }

        private void Cleanup()
        {
            _list = null;
            _preview?.Cleanup();
            _preview = null;
        }
    }
}
