using System;
using System.IO;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(EasyUIDisplay))]
    internal sealed class EasyUIDisplayInspector : UnityEditor.Editor
    {
        private SerializedProperty _viewTypeName;
        private SerializedProperty _viewScriptPath;
        private SerializedProperty _prefabLocationProperty;
        private MonoScript _viewScript;
        private string _validationError;
        private bool _needsTypeSync;
        private bool _showGenerator;
        private bool _showAdvancedConfiguration;
        private string _className;
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _prefabLocation;
        private string _scriptFolder = EasyUIScriptGenerator.DefaultScriptFolder;

        private void OnEnable()
        {
            _viewTypeName = serializedObject.FindProperty("_viewTypeName");
            _viewScriptPath = serializedObject.FindProperty("_viewScriptPath");
            _prefabLocationProperty = serializedObject.FindProperty("_prefabLocation");
            RefreshSelectedScript();

            var display = (EasyUIDisplay)target;
            Type selectedType = _viewScript?.GetClass();
            _className = selectedType == null
                ? display.name.Replace(" ", string.Empty)
                : selectedType.Name;
            _namespaceName = string.IsNullOrWhiteSpace(selectedType?.Namespace)
                ? EasyUIScriptGenerator.DefaultNamespace
                : selectedType.Namespace;
            _prefabLocation = display.PrefabLocation;
            if (!string.IsNullOrWhiteSpace(display.ViewScriptPath))
                _scriptFolder = Path.GetDirectoryName(display.ViewScriptPath)?.Replace('\\', '/');
        }

        public override void OnInspectorGUI()
        {
            var display = (EasyUIDisplay)target;
            serializedObject.Update();
            EditorGUILayout.LabelField("Easy UI Display", new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("View 类", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField(
                "View 类脚本",
                _viewScript,
                typeof(MonoScript),
                false);
            if (EditorGUI.EndChangeCheck()) ApplySelectedScript(selected);

            if (_viewScript != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("资源地址", display.PrefabLocation);
                EditorGUILayout.LabelField(
                    "资源地址来自 View 脚本上的 EasyUIPrefab 特性。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            if (EditorGUI.EndChangeCheck()) serializedObject.ApplyModifiedProperties();

            if (!string.IsNullOrEmpty(_validationError))
                EditorGUILayout.HelpBox(_validationError, MessageType.Error);
            else if (_needsTypeSync)
            {
                EditorGUILayout.HelpBox(
                    "Prefab 保存的类型名与所选脚本不一致。",
                    MessageType.Warning);
                if (GUILayout.Button("同步为所选脚本类型")) ApplySelectedScript(_viewScript);
            }
            else if (_viewScript == null)
                EditorGUILayout.HelpBox("请选择一个继承 EasyUIObject 的业务脚本。", MessageType.Info);

            EditorGUILayout.Space(6f);
            DrawDisplaySettings();
            serializedObject.ApplyModifiedProperties();

            _showGenerator = EditorGUILayout.Foldout(
                _showGenerator,
                "生成新的 View 脚本",
                true);
            if (_showGenerator)
            {
                EditorGUI.indentLevel++;
                _className = EditorGUILayout.TextField("类名", _className);
                _namespaceName = EditorGUILayout.TextField("命名空间", _namespaceName);
                _prefabLocation = EditorGUILayout.TextField("资源地址", _prefabLocation);
                DrawFolderField("脚本目录", ref _scriptFolder);
                if (GUILayout.Button("生成并选择脚本", GUILayout.Height(26f)))
                {
                    try
                    {
                        string path = EasyUIScriptGenerator.Generate(
                            display,
                            _className,
                            _namespaceName,
                            _prefabLocation,
                            _scriptFolder);
                        RefreshSelectedScript();
                        Debug.Log($"[Easy UI] 已生成 View：{path}", display);
                    }
                    catch (Exception exception) { Debug.LogException(exception, display); }
                }
                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button("校验 Display")) EasyUIValidator.ValidateAndReport(display);
            EditorGUILayout.HelpBox(
                "业务脚本通过对象框选择；类型名和资源地址会从脚本自动同步，不需要手动维护内部字符串。",
                MessageType.Info);
        }

        private void DrawDisplaySettings()
        {
            EditorGUILayout.LabelField("显示行为", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_managedAsView"), new GUIContent("受 UI Manager 管理"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_defaultLayer"), new GUIContent("默认层级"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_singleInstance"), new GUIContent("单例界面"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_closeOnBack"), new GUIContent("响应返回关闭"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_stretchToLayer"), new GUIContent("拉伸到层级尺寸"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("共享背景", EditorStyles.boldLabel);
            SerializedProperty backgroundMode = serializedObject.FindProperty("_backgroundMode");
            EditorGUILayout.PropertyField(backgroundMode, new GUIContent("背景模式"));
            if (backgroundMode.enumValueIndex != 0)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_closeOnBackground"), new GUIContent("点击背景关闭"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_backgroundColor"), new GUIContent("背景颜色"));
            }

            _showAdvancedConfiguration = EditorGUILayout.Foldout(
                _showAdvancedConfiguration,
                "高级配置",
                true);
            if (!_showAdvancedConfiguration) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_viewId"), new GUIContent("View Id（可选）"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_createOnAwake"), new GUIContent("Awake 时创建 Logic"));
            }
        }

        private static void DrawFolderField(string label, ref string value)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("选择…", GUILayout.Width(64f)))
            {
                string absolute = EditorUtility.OpenFolderPanel(label, Application.dataPath, string.Empty);
                if (!string.IsNullOrWhiteSpace(absolute))
                {
                    string relative = FileUtil.GetProjectRelativePath(absolute);
                    if (string.IsNullOrWhiteSpace(relative))
                        EditorUtility.DisplayDialog("Easy UI", "目录必须位于当前项目的 Assets 下。", "确定");
                    else
                        value = relative.TrimEnd('/', '\\');
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ApplySelectedScript(MonoScript script)
        {
            serializedObject.Update();
            _validationError = null;
            _needsTypeSync = false;
            if (script == null)
            {
                _viewScript = null;
                _viewTypeName.stringValue = string.Empty;
                _viewScriptPath.stringValue = string.Empty;
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (!TryValidateScript(script, (EasyUIDisplay)target, out Type type, out string error))
            {
                _validationError = error;
                return;
            }

            _viewScript = script;
            _viewTypeName.stringValue = GetStoredTypeName(type);
            _viewScriptPath.stringValue = AssetDatabase.GetAssetPath(script);
            if (EasyUIFactory.TryGetPrefabLocation(type, out string prefabLocation))
            {
                _prefabLocationProperty.stringValue = prefabLocation;
                _prefabLocation = prefabLocation;
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private void RefreshSelectedScript()
        {
            serializedObject.Update();
            _viewScript = null;
            _validationError = null;
            _needsTypeSync = false;
            string scriptPath = _viewScriptPath?.stringValue;
            if (!string.IsNullOrWhiteSpace(scriptPath))
                _viewScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

            if (_viewScript == null && !string.IsNullOrWhiteSpace(_viewTypeName?.stringValue))
            {
                Type storedType = Type.GetType(_viewTypeName.stringValue, false);
                if (storedType != null)
                {
                    string[] guids = AssetDatabase.FindAssets($"{storedType.Name} t:MonoScript");
                    for (int i = 0; i < guids.Length; i++)
                    {
                        MonoScript candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(
                            AssetDatabase.GUIDToAssetPath(guids[i]));
                        if (candidate != null && candidate.GetClass() == storedType)
                        {
                            _viewScript = candidate;
                            break;
                        }
                    }
                }
            }

            if (_viewScript == null) return;
            if (!TryValidateScript(_viewScript, (EasyUIDisplay)target, out Type type, out string error))
            {
                _validationError = error;
                return;
            }
            _needsTypeSync = !string.Equals(
                _viewTypeName.stringValue,
                GetStoredTypeName(type),
                StringComparison.Ordinal);
        }

        private static bool TryValidateScript(
            MonoScript script,
            EasyUIDisplay display,
            out Type type,
            out string error)
        {
            type = script.GetClass();
            if (type == null)
            {
                error = "所选脚本尚未成功编译，或脚本中没有与文件同名的类型。";
                return false;
            }
            if (!typeof(EasyUIObject).IsAssignableFrom(type))
            {
                error = $"“{type.FullName}”必须继承 EasyUIObject。";
                return false;
            }
            if (display.ManagedAsView && !typeof(EasyUIView).IsAssignableFrom(type))
            {
                error = $"“{type.FullName}”必须继承 EasyUIView 才能作为受管理 View。";
                return false;
            }
            if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"“{type.FullName}”必须是非抽象类型，并提供公开无参构造函数。";
                return false;
            }
            string path = AssetDatabase.GetAssetPath(script).Replace('\\', '/');
            if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "View 业务脚本不能放在 Editor 目录中。";
                return false;
            }

            error = null;
            return true;
        }

        private static string GetStoredTypeName(Type type) =>
            $"{type.FullName}, {type.Assembly.GetName().Name}";
    }
}
