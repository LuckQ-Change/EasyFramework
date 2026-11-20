using System;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(UIBindingRoot), true)]
    internal sealed class UIDisplayInspector : UnityEditor.Editor
    {
        private static readonly string[] DisplayKinds = { "Screen", "Popup", "Item" };
        private string _error;

        public override void OnInspectorGUI()
        {
            var root = (UIBindingRoot)target;
            serializedObject.Update();

            UIPageKind kind = root.Kind;
            if (root is UIDisplay)
                kind = (UIPageKind)EditorGUILayout.Popup("类型", Mathf.Clamp((int)kind, 0, DisplayKinds.Length - 1), DisplayKinds);
            else EditorGUILayout.LabelField("类型", "Node");

            string path = string.IsNullOrEmpty(root.ViewScriptGuid) ? null : AssetDatabase.GUIDToAssetPath(root.ViewScriptGuid);
            MonoScript script = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField("业务脚本", script, typeof(MonoScript), false);
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

            if (UIBindingRoot.IsPageKind(kind))
            {
                SerializedProperty address = serializedObject.FindProperty("_prefabLocation");
                address.stringValue = EditorGUILayout.DelayedTextField("资源地址", address.stringValue);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_closeOnBack"), new GUIContent("允许返回关闭"));
            }
            if (kind == UIPageKind.Popup)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_backgroundColor"), new GUIContent("遮罩颜色"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_closeOnBackground"), new GUIContent("点击遮罩关闭"));
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
            SerializedProperty address = serializedObject.FindProperty("_prefabLocation");
            string location = address?.stringValue;
            if (UIBindingRoot.IsPageKind(kind) && string.IsNullOrWhiteSpace(location) && view != null)
                location = UICreationWizard.LocationFor(view.Name);

            SerializedProperty kindProperty = serializedObject.FindProperty("_kind");
            if (kindProperty != null) kindProperty.enumValueIndex = (int)kind;
            serializedObject.FindProperty("_viewTypeName").stringValue = view?.FullName ?? string.Empty;
            serializedObject.FindProperty("_viewScriptGuid").stringValue = guid;

            if (address != null) address.stringValue = UIBindingRoot.IsPageKind(kind) ? location : string.Empty;
        }
    }
}
