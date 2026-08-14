using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(UIBindingContext))]
    internal sealed class UIBindingContextInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var context = (UIBindingContext)target;
            DrawBindingValidation(context);
            if (GUILayout.Button("自动收集 bind_ 节点"))
            {
                Undo.RecordObject(context, "Collect UI bindings");
                context.ReplaceBindings(UIBindingAutoCollector.Collect(context));
                EditorUtility.SetDirty(context);
            }
            EditorGUILayout.HelpBox(
                "命名规则：bind_Level 自动推断属性；bind_Level$Text 可显式指定 Text。响应绑定会随 EasyUIDisplay 一起生成到对应 XxxBinding.g.cs。",
                MessageType.Info);
        }

        private static void DrawBindingValidation(UIBindingContext context)
        {
            System.Type sourceType = UIBindingEditorReflection.ResolveSourceType(context);
            if (sourceType == null)
            {
                EditorGUILayout.HelpBox("Source 为空。运行时由 EasyUIDisplay 的纯 C# Logic 注入时可忽略。", MessageType.Info);
                return;
            }
            foreach (UIBindingDefinition binding in context.Bindings)
            {
                if (binding == null || binding.Target == null)
                {
                    EditorGUILayout.HelpBox("存在空的绑定目标。", MessageType.Error);
                    continue;
                }
                if (!UIBindingEditorReflection.HasBindableMember(sourceType, binding.SourceKey))
                    EditorGUILayout.HelpBox($"找不到响应属性：{binding.SourceKey}", MessageType.Error);
            }
        }
    }

    [CustomEditor(typeof(EasyUIStateController))]
    internal sealed class EasyUIStateControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("预览当前状态"))
            {
                var controller = (EasyUIStateController)target;
                Undo.RecordObjects(controller.GetComponentsInChildren<Component>(true), "Preview Easy UI state");
                controller.Refresh();
            }
            if (GUILayout.Button("打开集中状态工作台"))
                EasyUIStateWorkbench.Open((EasyUIStateController)target);
        }
    }

    [CustomEditor(typeof(EasyUIElement))]
    internal sealed class EasyUIElementInspector : UnityEditor.Editor
    {
        private SerializedProperty _controller;
        private SerializedProperty _captureDefaultOnAwake;
        private SerializedProperty _variants;

        private void OnEnable()
        {
            _controller = serializedObject.FindProperty("_controller");
            _captureDefaultOnAwake = serializedObject.FindProperty("_captureDefaultOnAwake");
            _variants = serializedObject.FindProperty("_variants");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_controller);
            EditorGUILayout.PropertyField(_captureDefaultOnAwake);
            EditorGUILayout.PropertyField(_variants, true);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            var element = (EasyUIElement)target;
            if (GUILayout.Button("将当前组件属性记录为默认值"))
            {
                Undo.RecordObject(element, "Capture Easy UI defaults");
                element.CaptureDefault();
                EditorUtility.SetDirty(element);
            }
            if (GUILayout.Button("预览 Controller 当前状态"))
            {
                Undo.RecordObjects(element.GetComponents<Component>(), "Preview Easy UI element");
                var controller = element.Controller;
                if (controller != null) element.ApplyState(controller.SelectedState);
            }
        }
    }
}
