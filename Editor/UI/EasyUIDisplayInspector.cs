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
        private MonoScript _viewScript;
        private string _validationError;
        private bool _needsTypeSync;
        private bool _showGenerator;
        private string _className;
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _prefabLocation;
        private string _scriptFolder = EasyUIScriptGenerator.DefaultScriptFolder;

        private void OnEnable()
        {
            _viewTypeName = serializedObject.FindProperty("_viewTypeName");
            _viewScriptPath = serializedObject.FindProperty("_viewScriptPath");
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
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_viewTypeName", "_viewScriptPath", "_prefabLocation");
            serializedObject.ApplyModifiedProperties();

            var display = (EasyUIDisplay)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("View 类", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField(
                "View 类脚本",
                _viewScript,
                typeof(MonoScript),
                false);
            if (EditorGUI.EndChangeCheck()) ApplySelectedScript(selected);

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
                _scriptFolder = EditorGUILayout.TextField("脚本目录", _scriptFolder);
                if (GUILayout.Button("生成并选择脚本"))
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
                "业务脚本由上方对象框直接选择；Prefab 只在运行时保存程序集限定类型名，不需要手动维护字符串。",
                MessageType.Info);
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
