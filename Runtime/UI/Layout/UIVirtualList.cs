using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("com.wjq.easyframework.editor")]
#endif

namespace EasyFramework.UI
{
    public enum UIVirtualListLayout { Vertical, Horizontal, Grid }

    public sealed class UIVirtualListRow<T>
    {
        public string Key { get; }
        public string Template { get; }
        public T Data { get; }
        public IReadOnlyList<UIVirtualListRow<T>> Children { get; }
        public bool InitiallyExpanded { get; }

        public UIVirtualListRow(string key, string template, T data,
            IReadOnlyList<UIVirtualListRow<T>> children = null, bool initiallyExpanded = false)
        {
            Key = key;
            Template = template;
            Data = data;
            Children = children ?? Array.Empty<UIVirtualListRow<T>>();
            InitiallyExpanded = initiallyExpanded;
        }
    }

    public readonly struct UIVirtualListItemContext
    {
        private readonly UIVirtualList _list;

        public string Key { get; }
        public int Index { get; }
        public int Depth { get; }
        public bool HasChildren { get; }
        public bool IsExpanded { get; }

        internal UIVirtualListItemContext(UIVirtualList list, string key, int index,
            int depth, bool hasChildren, bool expanded)
        {
            _list = list;
            Key = key;
            Index = index;
            Depth = depth;
            HasChildren = hasChildren;
            IsExpanded = expanded;
        }

        public void ToggleExpanded()
        {
            if (HasChildren && _list != null) _list.ToggleExpanded(Key);
        }
    }

    [AddComponentMenu("EasyFramework/UI/Virtual List")]
    [DisallowMultipleComponent, RequireComponent(typeof(ScrollRect))]
    public sealed class UIVirtualList : MonoBehaviour
    {
        [Serializable]
        private sealed class Template
        {
            public string Key;
            public RectTransform Prefab;
            [Min(1f)] public float Height = 32f;
            [Min(1f)] public float Width = 120f;
        }

        private sealed class Row
        {
            public string Key;
            public Template Template;
            public int Depth;
            public int Parent;
            public int End;
            public bool Expanded;
        }

        private struct Line
        {
            public int First;
            public int Count;
            public int Parent;
            public bool FullWidth;
            public float Offset;
            public float Size;
        }

        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private Template[] _templates = Array.Empty<Template>();
        [SerializeField] private UIVirtualListLayout _layout;
        [SerializeField] private RectOffset _padding = new();
        [SerializeField, Min(0f)] private float _spacing = 4f;
        [SerializeField, Min(0f)] private float _indent = 16f;
        [SerializeField, Min(0)] private int _overscan = 1;
        [SerializeField, Min(1)] private int _columns = 3;
        [SerializeField, Min(0f)] private float _columnSpacing = 4f;

        private List<Row> _rows = new();
        private Dictionary<string, int> _indices = new(StringComparer.Ordinal);
        private readonly List<int> _visible = new();
        private readonly List<int> _itemLines = new();
        private readonly List<Line> _lines = new();
        private readonly Dictionary<int, RectTransform> _active = new();
        private readonly Dictionary<Template, Stack<RectTransform>> _pool = new();
        private readonly List<int> _recycled = new();
        private Action<RectTransform, int, UIVirtualListItemContext> _bind;
        private object _data;
        private RectTransform _poolRoot;
        private float _contentLength;
        private int _created;
        private bool _rendering;

        public int ItemCount => _rows.Count;
        public int VisibleRowCount => _visible.Count;
        public int LiveItemCount => _active.Count;
        public int CreatedItemCount => _created;
        public UIVirtualListLayout Layout => _layout;

        internal RectTransform Viewport => _scrollRect == null ? null : _scrollRect.viewport;
        internal RectTransform Content => _scrollRect == null ? null : _scrollRect.content;
        private bool Horizontal => _layout == UIVirtualListLayout.Horizontal;

        internal void ValidateConfiguration() => ConfiguredTemplates();

        private Dictionary<string, Template> ConfiguredTemplates()
        {
            if (Viewport == null || Content == null || Viewport == transform ||
                !Viewport.IsChildOf(transform) || Content == Viewport || !Content.IsChildOf(Viewport))
                throw new InvalidOperationException("列表需要子节点 Viewport，以及 Viewport 内的 Content。");
            var templates = TemplateMap();
            if (templates.Count == 0) throw new InvalidOperationException("请至少配置一个 Item 模板。");
            if (Content.TryGetComponent(out LayoutGroup layout) && layout.enabled ||
                Content.TryGetComponent(out ContentSizeFitter fitter) && fitter.enabled &&
                (fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained ||
                 fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained))
                throw new InvalidOperationException("Content 由虚拟列表管理布局，请禁用其 Layout Group 或 Content Size Fitter。");
            return templates;
        }

#if UNITY_EDITOR
        private bool _editorPreview;
        private Action<RectTransform> _preparePreviewItem;
        internal IEnumerable<string> PreviewTemplateKeys => ConfiguredTemplates().Keys;
        internal void EnableEditorPreview(RectTransform inactivePool, Action<RectTransform> prepareItem)
        {
            _editorPreview = true;
            _poolRoot = inactivePool;
            _preparePreviewItem = prepareItem;
        }
        private bool CanRender => Application.isPlaying || _editorPreview;
#else
        private bool CanRender => Application.isPlaying;
#endif

        private void Reset() => _scrollRect = GetComponent<ScrollRect>();

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (_scrollRect != null) _scrollRect.onValueChanged.AddListener(OnScroll);
            ApplyContentLayout();
            Refresh();
        }

        private void OnDisable()
        {
            if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScroll);
            DestroyItems();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (CanRender && isActiveAndEnabled) Refresh();
        }

        private void OnScroll(Vector2 _) => Refresh();

        public void SetItems<T>(IReadOnlyList<UIVirtualListRow<T>> items) =>
            SetItems(items, static (rect, data, context) =>
            {
                if (!rect.TryGetComponent(out UIDisplay display))
                    throw new InvalidOperationException($"列表模板 {rect.name} 缺少 UIDisplay。");
                display.GetItem<UIListItem<T>>().Bind(data, context);
            });

        public void SetItems<T>(IReadOnlyList<UIVirtualListRow<T>> items,
            Action<RectTransform, T, UIVirtualListItemContext> bind)
        {
            CheckBinding();
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (bind == null) throw new ArgumentNullException(nameof(bind));
            var templates = ConfiguredTemplates();
            var rows = new List<Row>();
            var data = new List<T>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            AddRows(items, 0, -1);

            RecycleAll();
            _rows = rows;
            _indices = indices;
            _data = data;
            _bind = (rect, row, context) => bind(rect, data[row], context);
            UpdateLayout();

            void AddRows(IReadOnlyList<UIVirtualListRow<T>> source, int depth, int parent)
            {
                foreach (var item in source)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Key) || !indices.TryAdd(item.Key, rows.Count))
                        throw new InvalidOperationException($"列表项 Key 缺失或重复：{item?.Key}。");
                    if (item.Template == null || !templates.TryGetValue(item.Template, out Template template))
                        throw new InvalidOperationException($"列表项 {item.Key} 的模板 {item.Template} 未配置。");
                    bool expanded = item.InitiallyExpanded;
                    if (_indices.TryGetValue(item.Key, out int previous) && _rows[previous].End > previous + 1)
                        expanded = _rows[previous].Expanded;
                    var row = new Row { Key = item.Key, Template = template, Depth = depth, Parent = parent, Expanded = expanded };
                    int index = rows.Count;
                    rows.Add(row);
                    data.Add(item.Data);
                    AddRows(item.Children, depth + 1, index);
                    row.End = rows.Count;
                }
            }
        }

        /// <summary>按 Key 替换单项数据。屏幕外或已折叠的项下次显示时使用新数据。</summary>
        public bool UpdateItem<T>(string key, T data)
        {
            CheckBinding();
            if (key == null || !_indices.TryGetValue(key, out int index)) return false;
            if (_data is not List<T> values)
                throw new InvalidOperationException("UpdateItem 的数据类型需要与 SetItems 一致。");
            values[index] = data;
            RefreshRow(index);
            return true;
        }

        /// <summary>重新绑定单项，用于业务已修改该项引用类型数据的情况。</summary>
        public bool RefreshItem(string key)
        {
            CheckBinding();
            if (key == null || !_indices.TryGetValue(key, out int index)) return false;
            RefreshRow(index);
            return true;
        }

        private void RefreshRow(int entry)
        {
            int index = _visible.BinarySearch(entry);
            if (index < 0 || !_active.TryGetValue(index, out RectTransform item)) return;
            _rendering = true;
            try { BindRow(item, entry, index); }
            finally { _rendering = false; }
        }

        private Dictionary<string, Template> TemplateMap()
        {
            var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
            foreach (Template template in _templates)
            {
                float size = template == null ? 0f : Horizontal ? template.Width : template.Height;
                if (template == null || string.IsNullOrWhiteSpace(template.Key) || template.Prefab == null ||
                    !(size >= 1f) || float.IsInfinity(size))
                    throw new InvalidOperationException($"{name} 的列表模板需要 Key、Prefab 和有效的项宽或行高。");
                if (!templates.TryAdd(template.Key, template))
                    throw new InvalidOperationException($"{name} 的列表模板 Key {template.Key} 重复。");
            }
            return templates;
        }

        public bool SetExpanded(string key, bool expanded)
        {
            CheckBinding();
            if (key == null || !_indices.TryGetValue(key, out int index)) return false;
            Row row = _rows[index];
            if (row.End <= index + 1 || row.Expanded == expanded) return false;
            RecycleAll();
            row.Expanded = expanded;
            UpdateLayout();
            return true;
        }

        internal void ToggleExpanded(string key)
        {
            if (key != null && _indices.TryGetValue(key, out int index)) SetExpanded(key, !_rows[index].Expanded);
        }

        public void ScrollTo(int index)
        {
            if (Viewport == null || Content == null || _visible.Count == 0) return;
            int row = Mathf.Clamp(index, 0, _visible.Count - 1);
            SetOffset(row == 0 ? 0f : Mathf.Min(_lines[_itemLines[row]].Offset, MaxOffset));
            Refresh();
        }

        private void CheckBinding()
        {
            if (_rendering)
                throw new InvalidOperationException("请在列表项绑定回调结束后再修改列表数据或展开状态。");
        }

        private void UpdateLayout()
        {
            _visible.Clear();
            _itemLines.Clear();
            _lines.Clear();
            bool grid = _layout == UIVirtualListLayout.Grid;
            int columns = Mathf.Max(1, _columns);
            for (int i = 0; i < _rows.Count;)
            {
                Row row = _rows[i];
                bool hasChildren = row.End > i + 1;
                bool fullWidth = !grid || hasChildren;
                float size = Horizontal ? row.Template.Width : row.Template.Height;
                int last = _lines.Count - 1;
                if (!fullWidth && last >= 0 && !_lines[last].FullWidth &&
                    _lines[last].Parent == row.Parent && _lines[last].Count < columns)
                {
                    Line line = _lines[last];
                    line.Count++;
                    line.Size = Mathf.Max(line.Size, size);
                    _lines[last] = line;
                }
                else
                {
                    _lines.Add(new Line
                    {
                        First = _visible.Count, Count = 1, Parent = row.Parent, FullWidth = fullWidth, Size = size,
                    });
                }
                _itemLines.Add(_lines.Count - 1);
                _visible.Add(i);
                i = row.Expanded ? i + 1 : row.End;
            }

            float spacing = Mathf.Max(0f, _spacing);
            float offset = Mathf.Max(0, Horizontal ? _padding.left : _padding.top);
            for (int i = 0; i < _lines.Count; i++)
            {
                Line line = _lines[i];
                line.Offset = offset;
                _lines[i] = line;
                offset += line.Size + spacing;
            }
            _contentLength = _lines.Count == 0 ? 0f :
                offset - spacing + Mathf.Max(0, Horizontal ? _padding.right : _padding.bottom);
            ApplyContentLayout();
            Refresh();
        }

        private float ViewportLength => Horizontal ? Viewport.rect.width : Viewport.rect.height;
        private float MaxOffset => Mathf.Max(0f, _contentLength - ViewportLength);
        private float Offset => Horizontal ? -Content.anchoredPosition.x : Content.anchoredPosition.y;

        private void ApplyContentLayout()
        {
            if (!CanRender || Content == null || Viewport == null) return;
            float offset = Mathf.Clamp(Offset, 0f, MaxOffset);
            _scrollRect.horizontal = Horizontal;
            _scrollRect.vertical = !Horizontal;
            Content.anchorMin = Horizontal ? Vector2.zero : new Vector2(0f, 1f);
            Content.anchorMax = Horizontal ? new Vector2(0f, 1f) : Vector2.one;
            Content.pivot = new Vector2(0f, 1f);
            Content.sizeDelta = Horizontal ? new Vector2(_contentLength, 0f) : new Vector2(0f, _contentLength);
            SetOffset(offset);
        }

        private void SetOffset(float offset)
        {
            Vector2 position = Horizontal ? new Vector2(-offset, 0f) : new Vector2(0f, offset);
            if (Content.anchoredPosition != position) Content.anchoredPosition = position;
        }

        private int LineAt(float offset)
        {
            int low = 0;
            int high = _lines.Count - 1;
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (_lines[middle].Offset <= offset) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        public void Refresh()
        {
            if (!CanRender || !isActiveAndEnabled || _rendering || Viewport == null || Content == null ||
                _visible.Count == 0 || ViewportLength <= 0f) return;

            _rendering = true;
            try
            {
                float offset = Mathf.Clamp(Offset, 0f, MaxOffset);
                SetOffset(offset);
                int overscan = Mathf.Clamp(_overscan, 0, _lines.Count);
                Line firstLine = _lines[Mathf.Max(0, LineAt(offset) - overscan)];
                Line lastLine = _lines[Mathf.Min(_lines.Count - 1, LineAt(offset + ViewportLength) + overscan)];
                int first = firstLine.First;
                int last = lastLine.First + lastLine.Count - 1;
                _recycled.Clear();
                foreach (int index in _active.Keys)
                    if (index < first || index > last) _recycled.Add(index);
                foreach (int index in _recycled) Recycle(index);
                for (int index = first; index <= last; index++)
                {
                    if (!_active.TryGetValue(index, out RectTransform item))
                    {
                        int entry = _visible[index];
                        Row row = _rows[entry];
                        item = Rent(row.Template);
                        Place(item, index);
                        try
                        {
                            BindRow(item, entry, index);
                            item.gameObject.SetActive(true);
                            _active.Add(index, item);
                        }
                        catch
                        {
                            Return(item, row.Template);
                            throw;
                        }
                    }
                    else Place(item, index);
                }
            }
            finally { _rendering = false; }
        }

        private void BindRow(RectTransform item, int entry, int index)
        {
            Row row = _rows[entry];
            _bind(item, entry, new UIVirtualListItemContext(this, row.Key, index, row.Depth,
                row.End > entry + 1, row.Expanded));
        }

        private void Place(RectTransform item, int index)
        {
            Row row = _rows[_visible[index]];
            Line line = _lines[_itemLines[index]];
            Vector2 topLeft = new(0f, 1f);
            item.anchorMin = item.anchorMax = item.pivot = topLeft;
            float indent = row.Depth * Mathf.Max(0f, _indent);
            if (Horizontal)
            {
                float available = Mathf.Max(1f, Content.rect.height - Mathf.Max(0, _padding.bottom));
                float top = Mathf.Min(Mathf.Max(0, _padding.top) + indent, available - 1f);
                item.sizeDelta = new Vector2(row.Template.Width, available - top);
                item.anchoredPosition = new Vector2(line.Offset, -top);
                return;
            }

            float maxWidth = Mathf.Max(1f, Content.rect.width - Mathf.Max(0, _padding.right));
            float left = Mathf.Min(Mathf.Max(0, _padding.left) + indent, maxWidth - 1f);
            float width = maxWidth - left;
            if (!line.FullWidth)
            {
                int columns = Mathf.Max(1, _columns);
                float gap = columns == 1 ? 0f : Mathf.Min(Mathf.Max(0f, _columnSpacing),
                    Mathf.Max(0f, width - columns) / (columns - 1));
                width = Mathf.Max(0f, (width - gap * (columns - 1)) / columns);
                left += (index - line.First) * (width + gap);
            }
            item.sizeDelta = new Vector2(width, row.Template.Height);
            item.anchoredPosition = new Vector2(left, -line.Offset);
        }

        private RectTransform Rent(Template template)
        {
            RectTransform item;
            if (_pool.TryGetValue(template, out var items) && items.Count > 0)
                item = items.Pop();
            else
            {
                if (_poolRoot == null)
                {
                    var root = new GameObject("[List Pool]", typeof(RectTransform));
                    root.SetActive(false);
                    _poolRoot = (RectTransform)root.transform;
                    _poolRoot.SetParent(transform, false);
                }
                item = Instantiate(template.Prefab, _poolRoot, false);
                item.gameObject.SetActive(false);
#if UNITY_EDITOR
                _preparePreviewItem?.Invoke(item);
#endif
                _created++;
            }
            item.SetParent(Content, false);
            return item;
        }

        private void Recycle(int index)
        {
            RectTransform item = _active[index];
            _active.Remove(index);
            Return(item, _rows[_visible[index]].Template);
        }

        private void Return(RectTransform item, Template template)
        {
            if (item == null) return;
            if (item.TryGetComponent(out UIDisplay display)) display.RecycleItem();
            item.gameObject.SetActive(false);
            item.SetParent(_poolRoot, false);
            if (!_pool.TryGetValue(template, out var items))
                _pool.Add(template, items = new Stack<RectTransform>());
            items.Push(item);
        }

        private void RecycleAll()
        {
            foreach (var entry in _active) Return(entry.Value, _rows[_visible[entry.Key]].Template);
            _active.Clear();
        }

        private void DestroyItems()
        {
            foreach (RectTransform item in _active.Values)
                if (item != null) DestroyItem(item.gameObject);
            _active.Clear();
            _pool.Clear();
            if (_poolRoot != null) DestroyItem(_poolRoot.gameObject);
            _poolRoot = null;
        }

        private static void DestroyItem(GameObject item)
        {
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }
    }
}
