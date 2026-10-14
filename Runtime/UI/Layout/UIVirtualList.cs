using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    /// <summary>按可见行复用 UI Item；页面禁用时销毁实例并移除滚动监听。</summary>
    [AddComponentMenu("EasyFramework/UI/Virtual List")]
    [DisallowMultipleComponent]
    public sealed class UIVirtualList : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private RectTransform _itemTemplate;
        [SerializeField, Min(1f)] private float _itemHeight = 32f;
        [SerializeField, Min(0)] private int _itemCount = 80;
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private int _first = -1;
        private int _created;

        public int ItemCount => _itemCount;
        public int LiveItemCount => _items.Count;
        public int CreatedItemCount => _created;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (_scrollRect != null) _scrollRect.onValueChanged.AddListener(OnScrolled);
            Refresh();
        }

        private void OnDisable()
        {
            if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScrolled);
            Clear();
        }

        private void OnScrolled(Vector2 _) => Refresh();

        /// <summary>改变逻辑数据量，现有可见 Item 尽量复用。</summary>
        public void SetCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _itemCount = count;
            Refresh();
        }

        /// <summary>滚动到指定逻辑行；超出范围时停在可显示的最后一屏。</summary>
        public void ScrollTo(int index)
        {
            if (_content == null || _scrollRect == null || _scrollRect.viewport == null) return;
            float viewportHeight = _scrollRect.viewport.rect.height;
            float maxOffset = Mathf.Max(0f, _itemCount * _itemHeight - viewportHeight);
            float offset = Mathf.Clamp(index * _itemHeight, 0f, maxOffset);
            Vector2 position = _content.anchoredPosition;
            position.y = offset;
            _content.anchoredPosition = position;
            Refresh();
        }

        /// <summary>根据滚动位置重新绑定可见行，不为不可见数据创建 GameObject。</summary>
        public void Refresh()
        {
            if (!Application.isPlaying) return;
            if (_scrollRect == null || _scrollRect.viewport == null || _content == null ||
                _itemTemplate == null || _itemHeight <= 0f) return;
            float viewportHeight = Mathf.Max(0f, _scrollRect.viewport.rect.height);
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _itemCount * _itemHeight);
            float maxOffset = Mathf.Max(0f, _itemCount * _itemHeight - viewportHeight);
            float offset = Mathf.Clamp(_content.anchoredPosition.y, 0f, maxOffset);
            if (!Mathf.Approximately(offset, _content.anchoredPosition.y))
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, offset);
            int first = Mathf.Clamp(Mathf.FloorToInt(offset / _itemHeight),
                0, Mathf.Max(0, _itemCount - 1));
            int visible = Mathf.Min(_itemCount - first,
                Mathf.CeilToInt(viewportHeight / _itemHeight) + 2);
            visible = Mathf.Max(0, visible);
            bool countChanged = _items.Count != visible;
            while (_items.Count < visible)
            {
                RectTransform item = Instantiate(_itemTemplate, _content);
                item.gameObject.SetActive(true);
                item.name = "Visible Item";
                _items.Add(item);
                _created++;
            }
            while (_items.Count > visible)
            {
                int last = _items.Count - 1;
                RectTransform item = _items[last];
                _items.RemoveAt(last);
                DestroyItem(item);
            }
            if (_first == first && !countChanged) return;
            _first = first;
            for (int slot = 0; slot < _items.Count; slot++)
            {
                int row = first + slot;
                RectTransform item = _items[slot];
                item.anchoredPosition = new Vector2(0f, -row * _itemHeight);
                Text label = item.GetComponentInChildren<Text>(true);
                if (label != null) label.text = $"Item {row + 1:00}";
            }
        }

        private void Clear()
        {
            foreach (RectTransform item in _items) DestroyItem(item);
            _items.Clear();
            _first = -1;
        }

        private static void DestroyItem(RectTransform item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item.gameObject);
            else DestroyImmediate(item.gameObject);
        }
    }
}
