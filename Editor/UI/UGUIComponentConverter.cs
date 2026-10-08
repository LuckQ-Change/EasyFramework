using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    [InitializeOnLoad]
    internal static class UGUIComponentConverter
    {
        private sealed class ReferenceRecord
        {
            public UnityEngine.Object Owner;
            public string PropertyPath;
        }

        private static readonly Dictionary<Type, Type> ToExtended = new Dictionary<Type, Type>
        {
            { typeof(Image), typeof(UIImage) },
        };

        private static readonly Dictionary<Type, Type> ToUGUI = new Dictionary<Type, Type>
        {
            { typeof(UIImage), typeof(Image) },
        };
        private static readonly HashSet<Type> HeaderSwitchTypes = new HashSet<Type>
        {
            typeof(Image),
            typeof(UIImage),
        };

        static UGUIComponentConverter()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= DrawInspectorSwitch;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawInspectorSwitch;
        }

        private static void DrawInspectorSwitch(UnityEditor.Editor editor)
        {
            if (editor == null || editor.targets.Length != 1 || !(editor.target is Component component)) return;
            Type sourceType = component.GetType();
            if (!HeaderSwitchTypes.Contains(sourceType)) return;

            Type targetType;
            string label;
            string hint;
            if (ToExtended.TryGetValue(sourceType, out targetType))
            {
                label = "切换为 UIImage（图集索引）";
                hint = "仅 UIImage 提供 Sprite 列表和状态机可用的 Sprite Index。普通 UGUI Image 可直接使用，不必切换。";
            }
            else if (ToUGUI.TryGetValue(sourceType, out targetType))
            {
                label = "还原为原生 Image";
                hint = "还原后图集列表不会写回原生 Image。状态机组件如仍需要可继续留在节点上。";
            }
            else
            {
                return;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(hint, MessageType.None);
            if (!GUILayout.Button(label, EditorStyles.miniButton)) return;
            Component capturedComponent = component;
            Type capturedType = targetType;
            EditorApplication.delayCall += () =>
            {
                if (capturedComponent != null) Replace(capturedComponent, capturedType);
            };
        }

        [MenuItem("CONTEXT/Component/EasyFramework/转换为 UIImage（图集索引）", false, 2000)]
        private static void ConvertToExtended(MenuCommand command)
        {
            if (command.context is Component component && ToExtended.TryGetValue(component.GetType(), out var targetType))
                Replace(component, targetType);
        }

        [MenuItem("CONTEXT/Component/EasyFramework/转换为 UIImage（图集索引）", true)]
        private static bool ValidateConvertToExtended(MenuCommand command) =>
            command.context is Component component && ToExtended.ContainsKey(component.GetType());

        [MenuItem("CONTEXT/Component/EasyFramework/还原为原生 UGUI 组件", false, 2001)]
        private static void ConvertToUGUI(MenuCommand command)
        {
            if (command.context is Component component && ToUGUI.TryGetValue(component.GetType(), out var targetType))
                Replace(component, targetType);
        }

        [MenuItem("CONTEXT/Component/EasyFramework/还原为原生 UGUI 组件", true)]
        private static bool ValidateConvertToUGUI(MenuCommand command) =>
            command.context is Component component && ToUGUI.ContainsKey(component.GetType());

        internal static void ConvertSelection(bool toExtended)
        {
            GetSelectionImpact(toExtended, out int convertibleCount, out int referenceCount, false);
            if (convertibleCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "UI",
                    toExtended ? "当前选择层级中没有可转换的 Image。" : "当前选择层级中没有可还原的 UIImage。",
                    "确定");
                return;
            }

            string sourceName = toExtended ? "Image" : "UIImage";
            string targetName = toExtended ? "UIImage" : "原生 UGUI 组件";
            if (!EditorUtility.DisplayDialog(
                    "UI 替换确认",
                    $"将把选中层级中的 {convertibleCount} 个 {sourceName} 替换为 {targetName}。\n" +
                    $"当前已加载内容中发现 {referenceCount} 个引用。\n" +
                    "状态机（UIElement）不会随组件切换自动增删。",
                    "继续替换",
                    "取消"))
                return;

            var map = toExtended ? ToExtended : ToUGUI;
            int converted = 0;
            foreach (Component component in CollectSelection(toExtended))
            {
                if (component == null || !map.TryGetValue(component.GetType(), out var targetType)) continue;
                if (Replace(component, targetType)) converted++;
            }
            if (converted == 0)
            {
                EditorUtility.DisplayDialog(
                    "UI",
                    toExtended ? "当前选择层级中没有可转换的 Image。" : "当前选择层级中没有可还原的 UIImage。",
                    "确定");
            }
        }

        internal static void GetSelectionImpact(
            bool toExtended,
            out int convertibleCount,
            out int referenceCount,
            bool logDetails)
        {
            convertibleCount = 0;
            referenceCount = 0;
            foreach (Component component in CollectSelection(toExtended))
            {
                convertibleCount++;
                List<ReferenceRecord> references = CaptureReferences(component);
                referenceCount += references.Count;
                if (!logDetails) continue;
                Debug.Log(
                    $"[UI Impact] {GetHierarchyPath(component.transform)} / {component.GetType().Name}: " +
                    $"{references.Count} references",
                    component);
                foreach (ReferenceRecord reference in references)
                    Debug.Log($"  {reference.Owner.name}.{reference.PropertyPath}", reference.Owner);
            }
        }

        internal static int CountSelection(bool toExtended) => CollectSelection(toExtended).Count;

        private static List<Component> CollectSelection(bool toExtended)
        {
            var result = new List<Component>();
            var seen = new HashSet<Component>();
            Dictionary<Type, Type> map = toExtended ? ToExtended : ToUGUI;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (selected == null) continue;
                foreach (Component component in selected.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || !seen.Add(component)) continue;
                    if (map.ContainsKey(component.GetType())) result.Add(component);
                }
            }
            return result;
        }

        private static string GetHierarchyPath(Transform target)
        {
            string result = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                result = target.name + "/" + result;
            }
            return result;
        }

        private static bool Replace(Component source, Type targetType)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("UI", "组件切换只能在非运行状态执行。", "确定");
                return false;
            }
            if (!HasAttachableScript(targetType))
            {
                EditorUtility.DisplayDialog(
                    "UI",
                    $"找不到可挂载的 {targetType.Name}.cs。脚本文件名必须与组件类名一致，请等待 Unity 导入完成后重试。",
                    "确定");
                return false;
            }

            var references = CaptureReferences(source);
            GameObject gameObject = source.gameObject;
            string undoName = $"Convert {source.GetType().Name} to {targetType.Name}";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            try
            {
                if (!ComponentUtility.CopyComponent(source))
                    throw new InvalidOperationException($"Cannot copy {source.GetType().Name} values.");
                Undo.DestroyObjectImmediate(source);
                var replacement = Undo.AddComponent(gameObject, targetType);
                if (replacement == null || replacement.GetType() != targetType)
                    throw new InvalidOperationException($"Unity cannot attach {targetType.Name}.");
                if (!ComponentUtility.PasteComponentValues(replacement))
                    Debug.LogWarning($"[UI] {targetType.Name} 已替换，但部分原组件属性未能复制。", replacement);

                var element = gameObject.GetComponent<UIElement>();
                if (element != null) element.CaptureDefault();
                RestoreReferences(references, replacement);
                EditorUtility.SetDirty(gameObject);
                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeObject = replacement;
                return true;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception, gameObject);
                EditorUtility.DisplayDialog("UI", $"组件替换失败，原组件已恢复。\n{exception.Message}", "确定");
                return false;
            }
        }

        private static bool HasAttachableScript(Type targetType)
        {
            if (!typeof(IUIComponent).IsAssignableFrom(targetType)) return true;
            string[] guids = AssetDatabase.FindAssets($"{targetType.Name} t:MonoScript");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == targetType) return true;
            }
            return false;
        }

        private static List<ReferenceRecord> CaptureReferences(Component source)
        {
            var records = new List<ReferenceRecord>();
            foreach (var owner in EnumerateLoadedComponents(source.gameObject))
            {
                if (owner == null || owner == source) continue;
                try
                {
                    var serialized = new SerializedObject(owner);
                    var iterator = serialized.GetIterator();
                    bool enterChildren = true;
                    while (iterator.Next(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference ||
                            iterator.objectReferenceValue != source) continue;
                        records.Add(new ReferenceRecord { Owner = owner, PropertyPath = iterator.propertyPath });
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[UI] Inspecting references on {owner.name} failed: {exception.Message}");
                }
            }
            return records;
        }

        private static IEnumerable<Component> EnumerateLoadedComponents(GameObject source)
        {
            if (!source.scene.IsValid())
            {
                return source.transform.root.GetComponentsInChildren<Component>(true);
            }

            var result = new List<Component>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    result.AddRange(root.GetComponentsInChildren<Component>(true));
            }
            return result;
        }

        private static void RestoreReferences(IEnumerable<ReferenceRecord> records, Component replacement)
        {
            foreach (var record in records)
            {
                if (record.Owner == null) continue;
                var serialized = new SerializedObject(record.Owner);
                var property = serialized.FindProperty(record.PropertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Undo.RecordObject(record.Owner, "Restore UI references");
                property.objectReferenceValue = replacement;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(record.Owner);
            }
        }

    }
}
