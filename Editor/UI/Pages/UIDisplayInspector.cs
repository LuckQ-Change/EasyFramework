using System;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>编辑绑定根节点，并引导到已登记页面的清单配置。</summary>
    [CustomEditor(typeof(UIBindingRoot), true)]
    internal sealed class UIDisplayInspector : UnityEditor.Editor
    {
        private static readonly string[] DisplayKinds = { "Screen", "Popup", "Item" };
        private string _error;

        /// <summary>绘制 View 绑定和页面清单入口。</summary>
        public override void OnInspectorGUI()
        {
            var root = (UIBindingRoot)target;
            serializedObject.Update();

            UIPageManifest manifest = UIPageManifestAuthoring.FindManifest(root, out UIPageEntry entry);
            UIPageKind kind = root.Kind;
            if (root is UIDisplay)
            {
                using (new EditorGUI.DisabledScope(entry != null))
                    kind = (UIPageKind)EditorGUILayout.Popup("类型", Mathf.Clamp((int)kind, 0, DisplayKinds.Length - 1), DisplayKinds);
            }
            else EditorGUILayout.LabelField("类型", "Node");

            string path = string.IsNullOrEmpty(root.ViewScriptGuid) ? null : AssetDatabase.GUIDToAssetPath(root.ViewScriptGuid);
            MonoScript script = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            MonoScript selected;
            using (new EditorGUI.DisabledScope(entry != null))
                selected = (MonoScript)EditorGUILayout.ObjectField("业务脚本", script, typeof(MonoScript), false);
            if (kind != root.Kind || selected != script)
            {
                _error = null;
                try { SetBinding(kind, selected); }
                catch (InvalidOperationException exception)
                {
                    _error = exception.Message;
                    if (kind != root.Kind) SetBinding(kind, null);
                }
            }

            if (entry != null)
            {
                EditorGUILayout.LabelField("页面 ID", entry.Id);
                EditorGUILayout.LabelField("Prefab 地址", entry.PrefabAddress);
                if (GUILayout.Button($"打开页面清单 · {manifest.name}")) Selection.activeObject = manifest;
            }
            else if (UIBindingRoot.IsPageKind(kind))
            {
                EditorGUILayout.HelpBox("此 Prefab 尚未登记到页面清单。请通过创建页面向导生成可导航的页面。", MessageType.Info);
            }
            if (kind == UIPageKind.Popup)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_backgroundColor"), new GUIContent("遮罩颜色"));
            }
            serializedObject.ApplyModifiedProperties();

            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Warning);
            EditorGUILayout.HelpBox(root is UINode
                ? "使用继承 UINodeView 的脚本。放进页面后命名为 node_属性名，保存父 Prefab 即生成直接引用。"
                : "选择类型并关联业务脚本。控件按关键字命名，保存 Prefab 后同步强类型属性。",
                MessageType.Info);
        }

        private void SetBinding(UIPageKind kind, MonoScript script)
        {
            Type view = null;
            string guid = string.Empty;
            if (script != null)
            {
                string path = AssetDatabase.GetAssetPath(script);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("请选择项目 Assets 下的业务脚本。");
                view = script.GetClass();
                UIReferenceCodeGenerator.ViewArguments(view, kind);
                guid = AssetDatabase.AssetPathToGUID(path);
            }
            SerializedProperty kindProperty = serializedObject.FindProperty("_kind");
            if (kindProperty != null) kindProperty.enumValueIndex = (int)kind;
            serializedObject.FindProperty("_viewTypeName").stringValue = view?.FullName ?? string.Empty;
            serializedObject.FindProperty("_viewScriptGuid").stringValue = guid;

        }
    }
}
