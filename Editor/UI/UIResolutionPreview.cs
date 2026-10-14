using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>按 RectTransform 锚点计算不同分辨率的页面轮廓；只读取 Prefab 数据。</summary>
    internal sealed class UIResolutionPreview : EditorWindow
    {
        private struct PreviewRect
        {
            public Rect Rect;
            public RectTransform Transform;
            public int Depth;
        }

        private static readonly Vector2Int[] Presets = {
            new Vector2Int(1920, 1080), new Vector2Int(1366, 768),
            new Vector2Int(1080, 1920), new Vector2Int(1170, 2532) };
        private static readonly string[] PresetNames = {
            "1920 × 1080", "1366 × 768", "1080 × 1920", "1170 × 2532", "自定义" };
        private readonly List<PreviewRect> _rects = new List<PreviewRect>();
        private UIDisplay _display;
        private int _preset;
        private Vector2Int _resolution = new Vector2Int(1920, 1080);
        private Vector4 _safeInsets;

        internal static void Open(UIDisplay display)
        {
            var window = GetWindow<UIResolutionPreview>("UI 适配预览");
            window.minSize = new Vector2(480f, 420f);
            window._display = display;
            window.Show();
        }

        private void OnGUI()
        {
            _display = (UIDisplay)EditorGUILayout.ObjectField("页面", _display, typeof(UIDisplay), true);
            if (_display == null) return;
            int nextPreset = EditorGUILayout.Popup("分辨率", _preset, PresetNames);
            if (nextPreset != _preset)
            {
                _preset = nextPreset;
                if (_preset < Presets.Length) _resolution = Presets[_preset];
            }
            if (_preset == Presets.Length)
            {
                _resolution.x = Mathf.Max(1, EditorGUILayout.IntField("宽", _resolution.x));
                _resolution.y = Mathf.Max(1, EditorGUILayout.IntField("高", _resolution.y));
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("无安全区")) _safeInsets = Vector4.zero;
            if (GUILayout.Button("刘海屏示例"))
                _safeInsets = new Vector4(44f, 120f, 44f, 80f);
            EditorGUILayout.EndHorizontal();
            _safeInsets.x = Mathf.Max(0f, EditorGUILayout.FloatField("安全区左边距", _safeInsets.x));
            _safeInsets.y = Mathf.Max(0f, EditorGUILayout.FloatField("安全区上边距", _safeInsets.y));
            _safeInsets.z = Mathf.Max(0f, EditorGUILayout.FloatField("安全区右边距", _safeInsets.z));
            _safeInsets.w = Mathf.Max(0f, EditorGUILayout.FloatField("安全区下边距", _safeInsets.w));

            Rect area = GUILayoutUtility.GetRect(100f, 10000f, 260f, 10000f, GUILayout.ExpandWidth(true));
            float scale = Mathf.Min(area.width / _resolution.x, area.height / _resolution.y);
            var screen = new Rect(area.x + (area.width - _resolution.x * scale) * 0.5f,
                area.y + (area.height - _resolution.y * scale) * 0.5f,
                _resolution.x * scale, _resolution.y * scale);
            EditorGUI.DrawRect(screen, new Color(0.08f, 0.1f, 0.14f));
            DrawSafeArea(screen, scale);
            _rects.Clear();
            RectTransform root = _display.transform as RectTransform;
            if (root != null)
                Collect(root, new Rect(0f, 0f, _resolution.x, _resolution.y), 0);
            foreach (PreviewRect item in _rects)
            {
                if (item.Depth == 0) continue;
                Rect rect = ToScreen(item.Rect, screen, scale);
                if (!rect.Overlaps(screen)) continue;
                Color color = item.Transform.GetComponent<UIDisplay>() != null
                    ? new Color(0.21f, 0.82f, 0.65f, 0.75f)
                    : new Color(0.29f, 0.62f, 1f, 0.45f);
                DrawOutline(rect, color);
                if (rect.width > 70f && rect.height > 14f)
                    GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, rect.width - 4f, 16f),
                        item.Transform.name, EditorStyles.whiteMiniLabel);
            }
            EditorGUILayout.HelpBox("轮廓按 Anchor、Pivot、Size Delta 和位置计算；LayoutGroup 的实际排版请在 Game 视图核对。预览不会修改 Prefab。",
                MessageType.Info);
        }

        private void Collect(RectTransform parent, Rect parentRect, int depth)
        {
            _rects.Add(new PreviewRect { Rect = parentRect, Transform = parent, Depth = depth });
            foreach (Transform child in parent)
            {
                if (!(child is RectTransform rect)) continue;
                Vector2 anchorMin = rect.anchorMin;
                Vector2 anchorMax = rect.anchorMax;
                Vector2 sizeDelta = rect.sizeDelta;
                Vector2 anchoredPosition = rect.anchoredPosition;
                UISafeArea safeArea = rect.GetComponent<UISafeArea>();
                if (safeArea != null)
                {
                    Rect safePixels = new Rect(_safeInsets.x, _safeInsets.w,
                        Mathf.Max(0f, _resolution.x - _safeInsets.x - _safeInsets.z),
                        Mathf.Max(0f, _resolution.y - _safeInsets.y - _safeInsets.w));
                    UISafeArea.CalculateAnchors(safePixels, _resolution.x, _resolution.y,
                        safeArea.Left, safeArea.Right, safeArea.Top, safeArea.Bottom,
                        ref anchorMin, ref anchorMax);
                    sizeDelta = Vector2.zero;
                    anchoredPosition = Vector2.zero;
                }
                Vector2 size = Vector2.Scale(parentRect.size, anchorMax - anchorMin) + sizeDelta;
                Vector2 anchor = new Vector2(
                    Mathf.Lerp(anchorMin.x, anchorMax.x, rect.pivot.x),
                    Mathf.Lerp(anchorMin.y, anchorMax.y, rect.pivot.y));
                Vector2 bottomLeft = parentRect.position + Vector2.Scale(parentRect.size, anchor) +
                                     anchoredPosition - Vector2.Scale(size, rect.pivot);
                Collect(rect, new Rect(bottomLeft, size), depth + 1);
            }
        }

        private void DrawSafeArea(Rect screen, float scale)
        {
            float left = Mathf.Clamp(_safeInsets.x * scale, 0f, screen.width);
            float top = Mathf.Clamp(_safeInsets.y * scale, 0f, screen.height);
            float right = Mathf.Clamp(_safeInsets.z * scale, 0f, screen.width);
            float bottom = Mathf.Clamp(_safeInsets.w * scale, 0f, screen.height);
            var safe = new Rect(screen.x + left, screen.y + top,
                Mathf.Max(0f, screen.width - left - right),
                Mathf.Max(0f, screen.height - top - bottom));
            DrawOutline(safe, new Color(1f, 0.72f, 0.22f));
        }

        private static Rect ToScreen(Rect rect, Rect screen, float scale) =>
            new Rect(screen.x + rect.x * scale,
                screen.y + screen.height - (rect.y + rect.height) * scale,
                rect.width * scale, rect.height * scale);

        private static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
    }
}
