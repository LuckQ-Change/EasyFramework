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
    internal static class EasyUGUIComponentConverter
    {
        private sealed class ReferenceRecord
        {
            public UnityEngine.Object Owner;
            public string PropertyPath;
        }

        private static readonly Dictionary<Type, Type> ToEasy = new Dictionary<Type, Type>
        {
            { typeof(Image), typeof(EasyImage) },
            { typeof(RawImage), typeof(EasyRawImage) },
            { typeof(Text), typeof(EasyText) },
            { typeof(Button), typeof(EasyButton) },
            { typeof(Selectable), typeof(EasySelectable) },
            { typeof(Toggle), typeof(EasyToggle) },
            { typeof(Slider), typeof(EasySlider) },
            { typeof(Scrollbar), typeof(EasyScrollbar) },
            { typeof(Dropdown), typeof(EasyDropdown) },
            { typeof(InputField), typeof(EasyInputField) },
            { typeof(ScrollRect), typeof(EasyScrollRect) },
            { typeof(Mask), typeof(EasyMask) },
            { typeof(RectMask2D), typeof(EasyRectMask2D) },
            { typeof(HorizontalLayoutGroup), typeof(EasyHorizontalLayoutGroup) },
            { typeof(VerticalLayoutGroup), typeof(EasyVerticalLayoutGroup) },
            { typeof(GridLayoutGroup), typeof(EasyGridLayoutGroup) },
            { typeof(ContentSizeFitter), typeof(EasyContentSizeFitter) },
            { typeof(AspectRatioFitter), typeof(EasyAspectRatioFitter) },
            { typeof(CanvasScaler), typeof(EasyCanvasScaler) },
            { typeof(GraphicRaycaster), typeof(EasyGraphicRaycaster) },
            { typeof(Shadow), typeof(EasyShadow) },
            { typeof(Outline), typeof(EasyOutline) },
            { typeof(PositionAsUV1), typeof(EasyPositionAsUV1) },
            { typeof(LayoutElement), typeof(EasyLayoutElement) },
            { typeof(ToggleGroup), typeof(EasyToggleGroup) },
            { typeof(EventSystem), typeof(EasyEventSystem) },
            { typeof(EventTrigger), typeof(EasyEventTrigger) },
            { typeof(BaseInput), typeof(EasyBaseInput) },
            { typeof(StandaloneInputModule), typeof(EasyStandaloneInputModule) },
#pragma warning disable 618
            { typeof(TouchInputModule), typeof(EasyTouchInputModule) },
#pragma warning restore 618
            { typeof(PhysicsRaycaster), typeof(EasyPhysicsRaycaster) },
            { typeof(Physics2DRaycaster), typeof(EasyPhysics2DRaycaster) },
        };

        private static readonly Dictionary<Type, Type> ToUGUI = BuildReverseMap();

        static EasyUGUIComponentConverter()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= DrawInspectorSwitch;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawInspectorSwitch;
        }

        private static void DrawInspectorSwitch(UnityEditor.Editor editor)
        {
            if (editor == null || editor.targets.Length != 1 || !(editor.target is Component component)) return;

            Type targetType;
            string label;
            if (ToEasy.TryGetValue(component.GetType(), out targetType))
                label = "切换为 Easy UI 组件";
            else if (ToUGUI.TryGetValue(component.GetType(), out targetType))
                label = "还原为原生 UGUI 组件";
            else
                return;

            if (!GUILayout.Button(label, EditorStyles.miniButton)) return;
            Component capturedComponent = component;
            Type capturedType = targetType;
            EditorApplication.delayCall += () =>
            {
                if (capturedComponent != null) Replace(capturedComponent, capturedType);
            };
        }

        [MenuItem("CONTEXT/Component/EasyFramework/切换为 Easy UI 组件", false, 2000)]
        private static void ConvertToEasy(MenuCommand command)
        {
            if (command.context is Component component && ToEasy.TryGetValue(component.GetType(), out var targetType))
                Replace(component, targetType);
        }

        [MenuItem("CONTEXT/Component/EasyFramework/切换为 Easy UI 组件", true)]
        private static bool ValidateConvertToEasy(MenuCommand command) =>
            command.context is Component component && ToEasy.ContainsKey(component.GetType());

        [MenuItem("CONTEXT/Component/EasyFramework/还原为原生 UGUI 组件", false, 2001)]
        private static void ConvertToUGUI(MenuCommand command)
        {
            if (command.context is Component component && ToUGUI.TryGetValue(component.GetType(), out var targetType))
                Replace(component, targetType);
        }

        [MenuItem("CONTEXT/Component/EasyFramework/还原为原生 UGUI 组件", true)]
        private static bool ValidateConvertToUGUI(MenuCommand command) =>
            command.context is Component component && ToUGUI.ContainsKey(component.GetType());

        [MenuItem("Tools/EasyFramework/UI/切换选中的 UGUI 组件", false, 100)]
        private static void ConvertSelected()
        {
            GetSelectionImpact(out int convertibleCount, out int referenceCount, false);
            if (convertibleCount == 0)
            {
                EditorUtility.DisplayDialog("Easy UI", "当前选择中没有可切换的原生 UGUI 组件。", "确定");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Easy UI 替换确认",
                    $"将替换 {convertibleCount} 个组件；当前已加载内容中发现 {referenceCount} 个引用。\n" +
                    "建议先执行“扫描选中组件替换影响”查看明细。",
                    "继续替换",
                    "取消"))
                return;

            int converted = 0;
            foreach (var selected in Selection.gameObjects)
            {
                if (selected == null) continue;
                var components = selected.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null || !ToEasy.TryGetValue(component.GetType(), out var targetType)) continue;
                    if (Replace(component, targetType)) converted++;
                }
            }
            if (converted == 0) EditorUtility.DisplayDialog("Easy UI", "当前选择中没有可切换的原生 UGUI 组件。", "确定");
        }

        [MenuItem("Tools/EasyFramework/UI/扫描选中组件替换影响", false, 99)]
        private static void ScanSelectedImpact()
        {
            GetSelectionImpact(out int convertibleCount, out int referenceCount, true);
            EditorUtility.DisplayDialog(
                "Easy UI 替换影响",
                $"可替换组件：{convertibleCount}\n" +
                $"已加载场景 / 当前 Prefab 上下文引用：{referenceCount}\n\n" +
                "引用明细已输出到 Console。未加载的 Prefab 不在本报告范围内。",
                "确定");
        }

        private static void GetSelectionImpact(
            out int convertibleCount,
            out int referenceCount,
            bool logDetails)
        {
            convertibleCount = 0;
            referenceCount = 0;
            foreach (GameObject selected in Selection.gameObjects)
            {
                if (selected == null) continue;
                foreach (Component component in selected.GetComponents<Component>())
                {
                    if (component == null || !ToEasy.ContainsKey(component.GetType())) continue;
                    convertibleCount++;
                    List<ReferenceRecord> references = CaptureReferences(component);
                    referenceCount += references.Count;
                    if (!logDetails) continue;
                    Debug.Log(
                        $"[Easy UI Impact] {GetHierarchyPath(component.transform)} / {component.GetType().Name}: " +
                        $"{references.Count} references",
                        component);
                    foreach (ReferenceRecord reference in references)
                        Debug.Log($"  {reference.Owner.name}.{reference.PropertyPath}", reference.Owner);
                }
            }
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
                EditorUtility.DisplayDialog("Easy UI", "组件切换只能在非运行状态执行。", "确定");
                return false;
            }
            if (!HasAttachableScript(targetType))
            {
                EditorUtility.DisplayDialog(
                    "Easy UI",
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
                    Debug.LogWarning($"[Easy UI] {targetType.Name} 已替换，但部分原组件属性未能复制。", replacement);

                var element = gameObject.GetComponent<EasyUIElement>();
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
                EditorUtility.DisplayDialog("Easy UI", $"组件替换失败，原组件已恢复。\n{exception.Message}", "确定");
                return false;
            }
        }

        private static bool HasAttachableScript(Type targetType)
        {
            if (!typeof(IEasyUIComponent).IsAssignableFrom(targetType)) return true;
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
                    Debug.LogWarning($"[Easy UI] Inspecting references on {owner.name} failed: {exception.Message}");
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
                Undo.RecordObject(record.Owner, "Restore Easy UI references");
                property.objectReferenceValue = replacement;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(record.Owner);
            }
        }

        private static Dictionary<Type, Type> BuildReverseMap()
        {
            var result = new Dictionary<Type, Type>();
            foreach (var pair in ToEasy) result[pair.Value] = pair.Key;
            return result;
        }
    }
}
