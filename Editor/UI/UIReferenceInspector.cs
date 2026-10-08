using System;
using System.Collections.Generic;
using System.Text;
using EasyFramework.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(UIReference))]
    internal sealed class UIReferenceInspector : UnityEditor.Editor
    {
        private SerializedProperty _entries;
        private ReorderableList _list;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("_entries");
            _list = new ReorderableList(serializedObject, _entries, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "组件引用"),
                drawElementCallback = DrawEntry,
                elementHeightCallback = _ =>
                    EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing * 3f,
                onAddCallback = list =>
                {
                    int index = _entries.arraySize;
                    _entries.arraySize++;
                    SerializedProperty entry = _entries.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("_key").stringValue = string.Empty;
                    entry.FindPropertyRelative("_target").objectReferenceValue = null;
                    entry.FindPropertyRelative("_resourceLocation").stringValue = string.Empty;
                    list.index = index;
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("UI Reference", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.LabelField(
                "拖入组件后会自动生成唯一 Key；只在该引用需要动态加载资源时填写资源地址。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
            _list.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            DrawProblems();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("补全空 Key")) AutoFillEmptyKeys();
            if (GUILayout.Button("选择所属 Display")) SelectOwningDisplay();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntry(Rect rect, int index, bool active, bool focused)
        {
            if (index < 0 || index >= _entries.arraySize) return;
            SerializedProperty entry = _entries.GetArrayElementAtIndex(index);
            SerializedProperty key = entry.FindPropertyRelative("_key");
            SerializedProperty target = entry.FindPropertyRelative("_target");
            SerializedProperty resource = entry.FindPropertyRelative("_resourceLocation");

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            rect.y += spacing;
            float leftWidth = rect.width * 0.48f;
            var keyRect = new Rect(rect.x, rect.y, leftWidth, line);
            var targetRect = new Rect(rect.x + leftWidth + 6f, rect.y, rect.width - leftWidth - 6f, line);
            EditorGUI.PropertyField(keyRect, key, new GUIContent("Key"));

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(targetRect, target, new GUIContent("Target"));
            if (EditorGUI.EndChangeCheck() && target.objectReferenceValue is Component component &&
                string.IsNullOrWhiteSpace(key.stringValue))
            {
                key.stringValue = MakeUniqueKey(component, index);
            }

            var resourceRect = new Rect(rect.x, rect.y + line + spacing, rect.width, line);
            EditorGUI.PropertyField(resourceRect, resource, new GUIContent("资源地址（可选）"));
        }

        private void DrawProblems()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            bool hasEmpty = false;
            bool hasDuplicate = false;
            UIReference current = (UIReference)target;
            UIDisplay display = current.GetComponentInParent<UIDisplay>();
            UIReference[] markers = display == null
                ? new[] { current }
                : display.GetComponentsInChildren<UIReference>(true);
            foreach (UIReference marker in markers)
            {
                foreach (UIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null || string.IsNullOrWhiteSpace(entry.Key))
                        hasEmpty = true;
                    else if (!used.Add(entry.Key.Trim()))
                        hasDuplicate = true;
                }
            }

            if (hasEmpty)
                EditorGUILayout.HelpBox("存在未完成的引用：每条引用都需要 Key 和 Target。", MessageType.Error);
            if (hasDuplicate)
                EditorGUILayout.HelpBox("同一个 Display 内的 Key 必须唯一。", MessageType.Error);
        }

        private void AutoFillEmptyKeys()
        {
            Undo.RecordObject(target, "Fill UI reference keys");
            serializedObject.Update();
            for (int i = 0; i < _entries.arraySize; i++)
            {
                SerializedProperty entry = _entries.GetArrayElementAtIndex(i);
                SerializedProperty key = entry.FindPropertyRelative("_key");
                Component component = entry.FindPropertyRelative("_target").objectReferenceValue as Component;
                if (component != null && string.IsNullOrWhiteSpace(key.stringValue))
                    key.stringValue = MakeUniqueKey(component, i);
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private string MakeUniqueKey(Component component, int ignoreIndex)
        {
            string nodeName = SanitizeIdentifier(component.gameObject.name);
            string typeName = component.GetType().Name;
            string baseName = nodeName.EndsWith(typeName, StringComparison.OrdinalIgnoreCase)
                ? nodeName
                : nodeName + typeName;
            if (string.IsNullOrEmpty(baseName)) baseName = "Reference";

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _entries.arraySize; i++)
            {
                if (i == ignoreIndex) continue;
                string value = _entries.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_key").stringValue;
                if (!string.IsNullOrWhiteSpace(value)) used.Add(value.Trim());
            }
            UIReference current = (UIReference)target;
            UIDisplay display = current.GetComponentInParent<UIDisplay>();
            if (display != null)
            {
                foreach (UIReference marker in display.GetComponentsInChildren<UIReference>(true))
                {
                    if (marker == current) continue;
                    foreach (UIReferenceEntry entry in marker.Entries)
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.Key))
                            used.Add(entry.Key.Trim());
                }
            }
            if (!used.Contains(baseName)) return baseName;
            int suffix = 2;
            while (used.Contains(baseName + suffix)) suffix++;
            return baseName + suffix;
        }

        private static string SanitizeIdentifier(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
                if (char.IsLetterOrDigit(character) || character == '_') builder.Append(character);
            if (builder.Length > 0 && char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        private void SelectOwningDisplay()
        {
            UIDisplay display = ((UIReference)target).GetComponentInParent<UIDisplay>();
            if (display == null)
                EditorUtility.DisplayDialog("UI", "上级没有 UIDisplay。", "确定");
            else
                Selection.activeObject = display;
        }
    }
}
