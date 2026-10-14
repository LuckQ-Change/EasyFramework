using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    /// <summary>把常用 Anchor 与 Layout 配置直接写到所选 RectTransform，支持 Unity Undo。</summary>
    internal static class UIRectPresets
    {
        internal static void Stretch(RectTransform rect)
        {
            if (rect == null) return;
            Undo.RecordObject(rect, "Stretch UI rect");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            EditorUtility.SetDirty(rect);
        }

        internal static void Center(RectTransform rect)
        {
            if (rect == null) return;
            Undo.RecordObject(rect, "Center UI rect");
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            EditorUtility.SetDirty(rect);
        }

        internal static void TopLeft(RectTransform rect)
        {
            if (rect == null) return;
            Undo.RecordObject(rect, "Top left UI rect");
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            EditorUtility.SetDirty(rect);
        }

        internal static void AddVerticalLayout(GameObject node)
        {
            if (node == null || node.GetComponent<VerticalLayoutGroup>() != null) return;
            var layout = Undo.AddComponent<VerticalLayoutGroup>(node);
            Undo.RecordObject(layout, "Configure vertical UI layout");
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            EditorUtility.SetDirty(layout);
        }

        internal static void AddHorizontalLayout(GameObject node)
        {
            if (node == null || node.GetComponent<HorizontalLayoutGroup>() != null) return;
            var layout = Undo.AddComponent<HorizontalLayoutGroup>(node);
            Undo.RecordObject(layout, "Configure horizontal UI layout");
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            EditorUtility.SetDirty(layout);
        }

        internal static void AddGridLayout(GameObject node)
        {
            if (node == null || node.GetComponent<GridLayoutGroup>() != null) return;
            var layout = Undo.AddComponent<GridLayoutGroup>(node);
            Undo.RecordObject(layout, "Configure grid UI layout");
            layout.cellSize = new Vector2(200f, 64f);
            layout.spacing = new Vector2(8f, 8f);
            EditorUtility.SetDirty(layout);
        }
    }
}
