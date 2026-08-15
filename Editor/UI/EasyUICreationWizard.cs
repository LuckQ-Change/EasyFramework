using System.IO;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class EasyUICreationWizard : EditorWindow
    {
        private string _displayName = "InventoryView";
        private bool _managedAsView = true;
        private string _prefabFolder = "Assets/GameRes/UI";
        private string _prefabLocation = "UI/InventoryView";
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _scriptFolder = EasyUIScriptGenerator.DefaultScriptFolder;

        [MenuItem("Tools/EasyFramework/UI/Create Display", false, 1)]
        private static void Open() => GetWindow<EasyUICreationWizard>(true, "Create Easy UI Display");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Easy UI Display", EditorStyles.boldLabel);
            _displayName = EditorGUILayout.TextField("Name", _displayName);
            _managedAsView = EditorGUILayout.Toggle("Managed View", _managedAsView);
            _prefabFolder = EditorGUILayout.TextField("Prefab Path", _prefabFolder);
            _prefabLocation = EditorGUILayout.TextField("Resource Location", _prefabLocation);
            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);
            _scriptFolder = EditorGUILayout.TextField("Script Path", _scriptFolder);
            EditorGUILayout.Space();
            if (GUILayout.Button("Create Prefab And View")) Create();
        }

        private void Create()
        {
            Directory.CreateDirectory(Path.GetFullPath(_prefabFolder));
            AssetDatabase.Refresh();
            var root = new GameObject(_displayName, typeof(RectTransform), typeof(EasyUIDisplay));
            try
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(800f, 600f);
                var serialized = new SerializedObject(root.GetComponent<EasyUIDisplay>());
                serialized.FindProperty("_managedAsView").boolValue = _managedAsView;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                string prefabPath = _prefabFolder.TrimEnd('/', '\\') + "/" + _displayName + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                EasyUIDisplay display = prefab.GetComponent<EasyUIDisplay>();
                EasyUIScriptGenerator.Generate(
                    display,
                    _displayName,
                    _namespaceName,
                    _prefabLocation,
                    _scriptFolder);
                Selection.activeObject = prefab;
                Close();
            }
            finally { DestroyImmediate(root); }
        }
    }
}
