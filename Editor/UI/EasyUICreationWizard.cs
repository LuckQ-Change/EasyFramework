using System.IO;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class EasyUICreationWizard : EditorWindow
    {
        private string _displayName = "InventoryView";
        private string _prefabFolder = "Assets/GameRes/UI";
        private string _prefabLocation = "UI/InventoryView";
        private string _namespaceName = EasyUIScriptGenerator.DefaultNamespace;
        private string _baseType = "EasyUIView";
        private string _logicFolder = EasyUIScriptGenerator.DefaultLogicFolder;
        private string _bindingFolder = EasyUIScriptGenerator.DefaultBindingFolder;
        private bool _managedAsView = true;

        [MenuItem("Tools/EasyFramework/UI/Create Display", false, 1)]
        private static void Open() => GetWindow<EasyUICreationWizard>(true, "Create Easy UI Display");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("FGUI-style Display", EditorStyles.boldLabel);
            _displayName = EditorGUILayout.TextField("Class / Prefab Name", _displayName);
            _managedAsView = EditorGUILayout.Toggle("Managed As View", _managedAsView);
            _prefabFolder = EditorGUILayout.TextField("Prefab Path", _prefabFolder);
            _prefabLocation = EditorGUILayout.TextField("Prefab Location", _prefabLocation);
            _namespaceName = EditorGUILayout.TextField("Namespace", _namespaceName);
            if (_managedAsView && _baseType == "EasyUIItem") _baseType = "EasyUIView";
            if (!_managedAsView && _baseType == "EasyUIView") _baseType = "EasyUIItem";
            string[] baseTypes = EasyUIEditorTypeUtility.GetBusinessBaseTypes(_managedAsView);
            int selected = System.Array.IndexOf(baseTypes, _baseType);
            int next = EditorGUILayout.Popup("Business Base", Mathf.Max(0, selected), baseTypes);
            if (next >= 0 && next < baseTypes.Length && (selected >= 0 || string.IsNullOrWhiteSpace(_baseType)))
                _baseType = baseTypes[next];
            _baseType = EditorGUILayout.TextField("Custom Base", _baseType);
            _logicFolder = EditorGUILayout.TextField("Business Path", _logicFolder);
            _bindingFolder = EditorGUILayout.TextField("Binding Path", _bindingFolder);
            EditorGUILayout.Space();
            if (GUILayout.Button("Create Prefab And Scripts")) Create();
        }

        private void Create()
        {
            Directory.CreateDirectory(Path.GetFullPath(_prefabFolder));
            AssetDatabase.Refresh();
            var root = new GameObject(_displayName, typeof(RectTransform), typeof(EasyUIDisplay));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(800f, 600f);
                var display = root.GetComponent<EasyUIDisplay>();
                var serialized = new SerializedObject(display);
                serialized.FindProperty("_managedAsView").boolValue = _managedAsView;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string prefabPath = _prefabFolder.TrimEnd('/', '\\') + "/" + _displayName + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                EasyUIDisplay prefabDisplay = prefab.GetComponent<EasyUIDisplay>();
                EasyUIScriptGenerator.Generate(
                    prefabDisplay, _displayName, _namespaceName, _baseType,
                    _prefabLocation, _logicFolder, _bindingFolder);
                EditorUtility.SetDirty(prefabDisplay);
                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                Close();
            }
            finally
            {
                DestroyImmediate(root);
            }
        }
    }
}
