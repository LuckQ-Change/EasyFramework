using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>运行时把 RectTransform 拉伸到屏幕安全区；编辑期保持 Prefab 原始锚点。</summary>
    [AddComponentMenu("EasyFramework/UI/Safe Area")]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class UISafeArea : MonoBehaviour
    {
        [SerializeField] private bool _left = true;
        [SerializeField] private bool _right = true;
        [SerializeField] private bool _top = true;
        [SerializeField] private bool _bottom = true;
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private int _lastWidth;
        private int _lastHeight;

        public bool Left => _left;
        public bool Right => _right;
        public bool Top => _top;
        public bool Bottom => _bottom;

        private void OnEnable()
        {
            _rect = GetComponent<RectTransform>();
            ApplyIfChanged();
        }

        private void Update() => ApplyIfChanged();

        private void OnValidate()
        {
            // Reapply edited edge flags even when the screen dimensions did not change.
            _lastWidth = -1;
            if (Application.isPlaying) ApplyIfChanged();
        }

        private void ApplyIfChanged()
        {
            if (!Application.isPlaying || _rect == null) return;
            Rect safe = Screen.safeArea;
            if (_lastWidth == Screen.width && _lastHeight == Screen.height && safe == _lastSafeArea) return;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            _lastSafeArea = safe;
            Vector2 min = _rect.anchorMin;
            Vector2 max = _rect.anchorMax;
            CalculateAnchors(safe, Screen.width, Screen.height,
                _left, _right, _top, _bottom, ref min, ref max);
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }

        /// <summary>将像素安全区映射到锚点；未勾选的边保留调用方原始值。</summary>
        public static void CalculateAnchors(Rect safeArea, int width, int height,
            bool left, bool right, bool top, bool bottom, ref Vector2 min, ref Vector2 max)
        {
            if (width <= 0 || height <= 0) return;
            if (left) min.x = Mathf.Clamp01(safeArea.xMin / width);
            if (right) max.x = Mathf.Clamp01(safeArea.xMax / width);
            if (bottom) min.y = Mathf.Clamp01(safeArea.yMin / height);
            if (top) max.y = Mathf.Clamp01(safeArea.yMax / height);
        }
    }
}
