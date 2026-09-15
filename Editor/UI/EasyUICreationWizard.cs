using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class EasyUICreationWizard : EditorWindow
    {
        private const string PrefabFolderPref = "EasyFramework.UI.PrefabFolder";
        private const string NamespacePref = "EasyFramework.UI.Namespace";
        private const string ScriptFolderPref = "EasyFramework.UI.ScriptFolder";
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        private string _displayName = "InventoryView";
        private bool _managedAsView = true;
        private string _prefabFolder = "Assets/GameRes/UI";
        private string _prefabLocation = "UI/InventoryView";
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _scriptFolder = EasyUIScriptGenerator.DefaultScriptFolder;
        private bool _customLocation;
        private bool _showAdvanced;
        private string _error;

        [MenuItem("Tools/EasyFramework/UI/Create Display", false, 1)]
        private static void Open()
        {
            var window = GetWindow<EasyUICreationWizard>(true, "Create Easy UI Display");
            window.minSize = new Vector2(460f, 310f);
        }

        private void OnEnable()
        {
            _prefabFolder = EditorPrefs.GetString(PrefabFolderPref, _prefabFolder);
            _namespaceName = EditorPrefs.GetString(NamespacePref, _namespaceName);
            _scriptFolder = EditorPrefs.GetString(ScriptFolderPref, _scriptFolder);
            if (!_customLocation) _prefabLocation = BuildDefaultLocation(_displayName);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("创建 Easy UI", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
            EditorGUILayout.LabelField(
                "默认只需要填写界面名称，其余配置会自动推导并记住。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8f);

            EditorGUI.BeginChangeCheck();
            string nextName = EditorGUILayout.TextField(new GUIContent("界面名称", "同时作为 Prefab 和 View 类名"), _displayName);
            if (EditorGUI.EndChangeCheck())
            {
                bool followedDefault = !_customLocation ||
                    string.Equals(_prefabLocation, BuildDefaultLocation(_displayName), StringComparison.Ordinal);
                _displayName = nextName;
                if (followedDefault) _prefabLocation = BuildDefaultLocation(_displayName);
            }

            _managedAsView = EditorGUILayout.Toggle(
                new GUIContent("受 UI Manager 管理", "关闭后创建可嵌套的 EasyUIItem"),
                _managedAsView);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("将创建", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Prefab", GetPrefabPath());
                EditorGUILayout.LabelField("View", GetScriptPath());
                EditorGUILayout.LabelField("资源地址", _managedAsView ? EffectiveLocation : "（Item 不需要）");
            }

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "高级设置", true);
            if (_showAdvanced) DrawAdvancedSettings();

            _error = ValidateInput();
            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.HelpBox(_error, MessageType.Error);

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(_error)))
            {
                if (GUILayout.Button("创建 Prefab 和 View", GUILayout.Height(32f))) Create();
            }
            EditorGUILayout.Space(8f);
        }

        private string EffectiveLocation => _customLocation
            ? (_prefabLocation ?? string.Empty).Trim()
            : BuildDefaultLocation(_displayName);

        private void DrawAdvancedSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawFolderField("Prefab 目录", ref _prefabFolder);
                DrawFolderField("脚本目录", ref _scriptFolder);
                _namespaceName = EditorGUILayout.TextField("命名空间", _namespaceName);

                using (new EditorGUI.DisabledScope(!_managedAsView))
                {
                    _customLocation = EditorGUILayout.Toggle("自定义资源地址", _customLocation);
                    using (new EditorGUI.DisabledScope(!_customLocation))
                        _prefabLocation = EditorGUILayout.TextField("资源地址", EffectiveLocation);
                }
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
                        EditorUtility.DisplayDialog("Easy UI", "目录必须位于当前 Unity 项目的 Assets 下。", "确定");
                    else
                        value = relative.TrimEnd('/', '\\');
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_displayName)) return "请输入界面名称。";
            if (!IsIdentifier(_displayName.Trim()))
                return "界面名称必须是合法的 C# 类型名，只能包含字母、数字和下划线，且不能以数字开头。";
            if (!IsAssetFolder(_prefabFolder)) return "Prefab 目录必须位于 Assets 下。";
            if (!IsAssetFolder(_scriptFolder)) return "脚本目录必须位于 Assets 下。";
            if (!IsValidNamespace(_namespaceName)) return "命名空间格式无效，请使用以点分隔的 C# 标识符。";
            if (_managedAsView && string.IsNullOrWhiteSpace(EffectiveLocation)) return "受管理 View 必须有资源地址。";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GetPrefabPath()) != null)
                return $"Prefab 已存在：{GetPrefabPath()}";
            return null;
        }

        private void Create()
        {
            string prefabPath = GetPrefabPath();
            GameObject root = null;
            bool createdPrefab = false;
            try
            {
                Directory.CreateDirectory(Path.GetFullPath(_prefabFolder));
                AssetDatabase.Refresh();
                root = new GameObject(_displayName.Trim(), typeof(RectTransform), typeof(EasyUIDisplay));
                ((RectTransform)root.transform).sizeDelta = new Vector2(800f, 600f);
                var serialized = new SerializedObject(root.GetComponent<EasyUIDisplay>());
                serialized.FindProperty("_managedAsView").boolValue = _managedAsView;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Unity 未能创建 Prefab，请检查目标目录。");
                createdPrefab = true;
                EasyUIDisplay display = prefab.GetComponent<EasyUIDisplay>();
                EasyUIScriptGenerator.Generate(
                    display,
                    _displayName.Trim(),
                    _namespaceName,
                    _managedAsView ? EffectiveLocation : string.Empty,
                    _scriptFolder);

                EditorPrefs.SetString(PrefabFolderPref, _prefabFolder);
                EditorPrefs.SetString(NamespacePref, _namespaceName);
                EditorPrefs.SetString(ScriptFolderPref, _scriptFolder);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Close();
            }
            catch (Exception exception)
            {
                if (createdPrefab) AssetDatabase.DeleteAsset(prefabPath);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Easy UI 创建失败", exception.Message, "确定");
            }
            finally
            {
                if (root != null) DestroyImmediate(root);
            }
        }

        private string GetPrefabPath() =>
            NormalizeFolder(_prefabFolder) + "/" + (_displayName ?? string.Empty).Trim() + ".prefab";

        private string GetScriptPath() =>
            NormalizeFolder(_scriptFolder) + "/" + MakeTypeName(_displayName) + ".cs";

        private static string BuildDefaultLocation(string displayName) =>
            "UI/" + MakeTypeName(displayName);

        private static string NormalizeFolder(string path) =>
            (path ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');

        private static bool IsAssetFolder(string path)
        {
            string normalized = NormalizeFolder(path);
            return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static string MakeTypeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder();
            foreach (char character in value.Trim())
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            if (builder.Length > 0 && char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        private static bool IsValidNamespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Trim().Split('.');
            for (int i = 0; i < parts.Length; i++)
                if (!IsIdentifier(parts[i])) return false;
            return true;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || CSharpKeywords.Contains(value)) return false;
            if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
            for (int i = 1; i < value.Length; i++)
                if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_')) return false;
            return true;
        }
    }
}
