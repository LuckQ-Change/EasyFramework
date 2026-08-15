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
        private string _prefabLocation;
        private string _scriptFolder = EasyUIScriptGenerator.DefaultScriptFolder;

        private void OnEnable()
        {
            var display = (EasyUIDisplay)target;
            _className = string.IsNullOrWhiteSpace(display.ViewTypeName)
                ? display.name.Replace(" ", string.Empty)
                : display.ViewTypeName.Substring(display.ViewTypeName.LastIndexOf('.') + 1);
            _namespaceName = string.IsNullOrWhiteSpace(display.ViewTypeName) ||
                             !display.ViewTypeName.Contains(".")
                ? EasyUIScriptGenerator.DefaultNamespace
                : display.ViewTypeName.Substring(0, display.ViewTypeName.LastIndexOf('.'));
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
            EditorGUILayout.LabelField("View Script", EditorStyles.boldLabel);
            _className = EditorGUILayout.TextField("Class", _className);
            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);
            _prefabLocation = EditorGUILayout.TextField("Resource Location", _prefabLocation);
            _scriptFolder = EditorGUILayout.TextField("Script Path", _scriptFolder);

            if (!string.IsNullOrWhiteSpace(display.ViewScriptPath))
                EditorGUILayout.LabelField("Current Script", display.ViewScriptPath);

            if (GUILayout.Button("Generate View Script"))
            {
                try
                {
                    string path = EasyUIScriptGenerator.Generate(
                        display,
                        _className,
                        _namespaceName,
                        _prefabLocation,
                        _scriptFolder);
                    Debug.Log($"[Easy UI] View generated: {path}", display);
                }
                catch (Exception exception) { Debug.LogException(exception, display); }
            }

            if (GUILayout.Button("Validate Display")) EasyUIValidator.ValidateAndReport(display);
            EditorGUILayout.HelpBox(
                "Only one View script is generated. Add EasyUIReference entries on the prefab and use Binding.Get<T>(key) in the View.",
                MessageType.Info);
        }
    }
}
