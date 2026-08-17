using System;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(EasyFrameworkStartupConfig))]
    internal sealed class EasyFrameworkStartupConfigInspector : UnityEditor.Editor
    {
        private SerializedProperty _startupProcedureTypeName;
        private MonoScript _startupProcedureScript;
        private string _validationError;

        private void OnEnable()
        {
            _startupProcedureTypeName = serializedObject.FindProperty("_startupProcedureTypeName");
            RefreshScript();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("启动流程", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField(
                "启动流程脚本",
                _startupProcedureScript,
                typeof(MonoScript),
                false);
            if (EditorGUI.EndChangeCheck()) ApplySelectedScript(selected);

            if (!string.IsNullOrEmpty(_validationError))
                EditorGUILayout.HelpBox(_validationError, MessageType.Error);
            else if (_startupProcedureScript == null)
                EditorGUILayout.HelpBox("未配置时不会自动注册或启动流程模块。", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Launcher 会自动启动该流程；Next 和 Change 遇到的新流程会按需注册。",
                    MessageType.Info);

            EditorGUILayout.Space();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "_startupProcedureTypeName");
            serializedObject.ApplyModifiedProperties();
        }

        private void ApplySelectedScript(MonoScript script)
        {
            _startupProcedureScript = script;
            _validationError = null;
            if (script == null)
            {
                _startupProcedureTypeName.stringValue = string.Empty;
                return;
            }

            if (!TryValidateScript(script, out Type type, out string error))
            {
                _validationError = error;
                return;
            }

            _startupProcedureTypeName.stringValue =
                $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        private void RefreshScript()
        {
            _startupProcedureScript = null;
            _validationError = null;
            string typeName = _startupProcedureTypeName?.stringValue;
            if (string.IsNullOrWhiteSpace(typeName)) return;

            Type type = Type.GetType(typeName, false);
            if (type == null)
            {
                _validationError = $"无法加载已配置的启动流程类型：{typeName}";
                return;
            }

            string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type)
                {
                    _startupProcedureScript = script;
                    return;
                }
            }

            _validationError = $"找不到启动流程“{type.FullName}”对应的脚本资源。";
        }

        private static bool TryValidateScript(MonoScript script, out Type type, out string error)
        {
            type = script.GetClass();
            if (type == null)
            {
                error = "所选脚本尚未成功编译，或脚本中没有与文件同名的类型。";
                return false;
            }
            if (!typeof(ProcedureBase).IsAssignableFrom(type))
            {
                error = $"“{type.FullName}”必须继承 ProcedureBase。";
                return false;
            }
            if (type.IsAbstract)
            {
                error = $"“{type.FullName}”是抽象类，不能作为启动流程。";
                return false;
            }
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"“{type.FullName}”必须提供公开的无参构造函数。";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(script).Replace('\\', '/');
            if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "启动流程不能放在 Editor 目录中，否则 Player 构建无法加载。";
                return false;
            }

            error = null;
            return true;
        }
    }

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
                "命名规则：bind_Level 自动推断属性；bind_Level$Text 可显式指定 Text。响应绑定保存在 Prefab 的 UIBindingContext 中，不生成脚本。",
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
