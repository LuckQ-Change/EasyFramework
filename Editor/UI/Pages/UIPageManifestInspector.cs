using System;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>查看页面登记并编辑资源地址与导航策略。</summary>
    [CustomEditor(typeof(UIPageManifest))]
    internal sealed class UIPageManifestInspector : UnityEditor.Editor
    {
        private string _message;
        private bool _hasError;
        private int _draftIndex = -1;
        private string _draftSource;
        private string _draftPrefab;
        private string _draftAddress;

        /// <summary>绘制页面登记项和校验入口。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            (int index, string address)? edit;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                edit = DrawPages(serializedObject.FindProperty("_pages"));
            if (serializedObject.ApplyModifiedProperties()) _message = null;
            if (edit.HasValue) PreviewAndApply((UIPageManifest)target, edit.Value.index, edit.Value.address);

            EditorGUILayout.Space();
            if (GUILayout.Button("校验登记"))
            {
                try
                {
                    ((UIPageManifest)target).Validate();
                    _message = "页面登记校验通过。";
                    _hasError = false;
                }
                catch (InvalidOperationException exception)
                {
                    _message = exception.Message;
                    _hasError = true;
                }
            }
            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, _hasError ? MessageType.Error : MessageType.Info);
        }

        private (int index, string address)? DrawPages(SerializedProperty pages)
        {
            EditorGUILayout.LabelField("页面登记", $"{pages.arraySize} 项", EditorStyles.boldLabel);
            for (int i = 0; i < pages.arraySize; i++)
            {
                SerializedProperty page = pages.GetArrayElementAtIndex(i);
                string id = page.FindPropertyRelative("_id").stringValue;
                page.isExpanded = EditorGUILayout.Foldout(page.isExpanded, string.IsNullOrEmpty(id) ? "缺少页面 ID" : id, true);
                if (!page.isExpanded) continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    string prefabGuid = page.FindPropertyRelative("_prefabGuid").stringValue;
                    string factoryKey = page.FindPropertyRelative("_factoryKey").stringValue;
                    var kind = (UIPageKind)page.FindPropertyRelative("_kind").intValue;
                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
                    string factoryPath = AssetDatabase.GUIDToAssetPath(factoryKey);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Prefab", AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),
                            typeof(GameObject), false);
                        EditorGUILayout.ObjectField("工厂 View", AssetDatabase.LoadAssetAtPath<MonoScript>(factoryPath),
                            typeof(MonoScript), false);
                        EditorGUILayout.TextField("导航类型", kind.ToString());
                    }
                    if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(factoryPath))
                        EditorGUILayout.HelpBox("登记中的 Prefab 或工厂脚本已丢失。", MessageType.Warning);
                    string currentAddress = page.FindPropertyRelative("_prefabAddress").stringValue;
                    string draft = DrawAddress(i, prefabGuid, currentAddress);
                    EditorGUILayout.PropertyField(page.FindPropertyRelative("_isCloseOnBack"), new GUIContent("返回关闭"));
                    if (kind == UIPageKind.Popup)
                        EditorGUILayout.PropertyField(page.FindPropertyRelative("_isCloseOnBackground"), new GUIContent("遮罩点击关闭"));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(draft == currentAddress))
                            if (GUILayout.Button("预览地址变更")) return (i, draft);
                        if (GUILayout.Button("预览移除登记")) return (i, null);
                    }
                }
            }
            return null;
        }

        private string DrawAddress(int index, string prefabGuid, string currentAddress)
        {
            bool hasDraft = _draftIndex == index && _draftPrefab == prefabGuid && _draftSource == currentAddress;
            string previous = hasDraft ? _draftAddress : currentAddress;
            string address = EditorGUILayout.TextField("Prefab 地址", previous);
            if (address != previous)
            {
                _draftIndex = index;
                _draftSource = currentAddress;
                _draftPrefab = prefabGuid;
                _draftAddress = address;
            }
            return address;
        }

        private void PreviewAndApply(UIPageManifest manifest, int index, string address)
        {
            try
            {
                UIPageManifestAuthoring.PageChange preview = UIPageManifestAuthoring.PreviewPageChange(manifest, index, address);
                string title = address == null ? "预览移除登记" : "预览地址变更";
                if (!EditorUtility.DisplayDialog(title, preview.Summary, "应用", "取消")) return;

                UIPageManifestAuthoring.ApplyPageChange(preview);
                _draftIndex = -1;
                _message = preview.Summary;
                _hasError = false;
                serializedObject.Update();
            }
            catch (Exception exception)
            {
                _message = exception is AggregateException aggregate
                    ? $"{exception.Message}\n{string.Join("\n", aggregate.Flatten().InnerExceptions.Select(error => error.GetBaseException().Message))}"
                    : exception.Message;
                _hasError = true;
                serializedObject.Update();
            }
        }
    }
}
