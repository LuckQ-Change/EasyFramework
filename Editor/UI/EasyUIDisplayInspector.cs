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
        private string _className;
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _baseTypeName;
        private string _prefabLocation;
        private string _logicFolder = EasyUIScriptGenerator.DefaultLogicFolder;
        private string _bindingFolder = EasyUIScriptGenerator.DefaultBindingFolder;

        private void OnEnable()
        {
            var display = (EasyUIDisplay)target;
            _className = string.IsNullOrWhiteSpace(display.Scripts.LogicTypeName)
                ? display.name.Replace(" ", string.Empty)
                : display.Scripts.LogicTypeName.Substring(display.Scripts.LogicTypeName.LastIndexOf('.') + 1);
            _baseTypeName = string.IsNullOrWhiteSpace(display.Scripts.LogicBaseTypeName)
                ? (display.ManagedAsView ? "EasyUIView" : "EasyUIItem")
                : display.Scripts.LogicBaseTypeName;
            _prefabLocation = display.PrefabLocation;
            if (!string.IsNullOrWhiteSpace(display.Scripts.LogicScriptPath))
                _logicFolder = Path.GetDirectoryName(display.Scripts.LogicScriptPath)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(display.Scripts.BindingScriptPath))
                _bindingFolder = Path.GetDirectoryName(display.Scripts.BindingScriptPath)?.Replace('\\', '/');
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_scripts", "_prefabLocation");
            serializedObject.ApplyModifiedProperties();

            var display = (EasyUIDisplay)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pure C# Scripts", EditorStyles.boldLabel);
            _className = EditorGUILayout.TextField("Class Name", _className);
            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);
            _baseTypeName = DrawBaseType(_baseTypeName, display.ManagedAsView);
            _prefabLocation = EditorGUILayout.TextField("Prefab Location", _prefabLocation);
            _logicFolder = EditorGUILayout.TextField("Business Path", _logicFolder);
            _bindingFolder = EditorGUILayout.TextField("Binding Path", _bindingFolder);

            if (!string.IsNullOrWhiteSpace(display.Scripts.RecordId))
            {
                EditorGUILayout.LabelField("Record ID", display.Scripts.RecordId);
                EditorGUILayout.LabelField("Business Script", display.Scripts.LogicScriptPath);
                EditorGUILayout.LabelField("Generated Binding", display.Scripts.BindingScriptPath);
                EditorGUILayout.LabelField("Display Signature", display.Scripts.DisplaySignature);
            }

            if (GUILayout.Button("生成 / 更新 Display 脚本"))
            {
                try
                {
                    string path = EasyUIScriptGenerator.Generate(
                        display, _className, _namespaceName, _baseTypeName,
                        _prefabLocation, _logicFolder, _bindingFolder);
                    Debug.Log($"[Easy UI] Display 脚本已生成：{path}", display);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, display);
                    EditorUtility.DisplayDialog("Easy UI", exception.Message, "确定");
                }
            }

            if (GUILayout.Button("校验 Display"))
                EasyUIValidator.ValidateAndReport(display);

            EditorGUILayout.HelpBox(
                "Prefab 只挂 EasyUIDisplay。业务脚本位于 Business Path，可继承；Binding 位于 Generated Path，只读。Reference Marker 上的 Resource Location 会作为特性和常量一起生成。",
                MessageType.Info);
        }

        private static string DrawBaseType(string current, bool managedAsView)
        {
            if (managedAsView && current == "EasyUIItem") current = "EasyUIView";
            if (!managedAsView && current == "EasyUIView") current = "EasyUIItem";
            string[] options = EasyUIEditorTypeUtility.GetBusinessBaseTypes(managedAsView);
            int selected = Array.IndexOf(options, current);
            int next = EditorGUILayout.Popup("Business Base", Mathf.Max(0, selected), options);
            if (next >= 0 && next < options.Length && (selected >= 0 || string.IsNullOrWhiteSpace(current)))
                current = options[next];
            return EditorGUILayout.TextField("Custom Base", current);
        }
    }
}
