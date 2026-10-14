using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    /// <summary>在真实 Prefab 的引用组件上添加条目，保持引用数据只有一个来源。</summary>
    internal static class UIReferenceEditorActions
    {
        internal static UIReference Expose(UIDisplay display, GameObject node)
        {
            if (display == null || node == null || AssetDatabase.Contains(node) ||
                node.GetComponentInParent<UIDisplay>(true) != display)
                throw new InvalidOperationException("请在当前 Display 的 Prefab Mode 中选择节点。");
            Component component = ChooseComponent(node);
            if (component == null) throw new InvalidOperationException("选中节点没有可暴露的 UI 组件。");
            UIReference marker = node.GetComponent<UIReference>() ?? Undo.AddComponent<UIReference>(node);
            if (marker.Entries.Any(entry => entry != null && entry.Target == component)) return marker;
            string key = MakeUniqueKey(display, component);
            Undo.RecordObject(marker, "Expose UI reference");
            var serialized = new SerializedObject(marker);
            SerializedProperty entries = serialized.FindProperty("_entries");
            int index = entries.arraySize;
            entries.arraySize++;
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("_key").stringValue = key;
            entry.FindPropertyRelative("_target").objectReferenceValue = component;
            entry.FindPropertyRelative("_resourceLocation").stringValue = string.Empty;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(marker);
            return marker;
        }

        private static Component ChooseComponent(GameObject node)
        {
            return (Component)node.GetComponent<Selectable>() ??
                   (Component)node.GetComponent<Graphic>() ??
                   node.GetComponent<RectTransform>();
        }

        private static string MakeUniqueKey(UIDisplay display, Component component)
        {
            var name = new StringBuilder();
            foreach (char c in component.gameObject.name)
                if (char.IsLetterOrDigit(c) || c == '_') name.Append(c);
            if (name.Length == 0 || char.IsDigit(name[0])) name.Insert(0, '_');
            string typeName = component.GetType().Name;
            string baseName = name.ToString();
            if (!baseName.EndsWith(typeName, StringComparison.OrdinalIgnoreCase)) baseName += typeName;
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (UIReference marker in display.GetComponentsInChildren<UIReference>(true))
            {
                if (marker.GetComponentInParent<UIDisplay>(true) != display) continue;
                foreach (UIReferenceEntry entry in marker.Entries)
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Key)) used.Add(entry.Key.Trim());
            }
            if (!used.Contains(baseName)) return baseName;
            int suffix = 2;
            while (used.Contains(baseName + suffix)) suffix++;
            return baseName + suffix;
        }
    }
}
