using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(EasyUIReference))]
    internal sealed class EasyUIReferenceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var marker = (EasyUIReference)target;
            foreach (EasyUIReferenceEntry entry in marker.Entries)
            {
                if (entry == null || entry.Target == null || string.IsNullOrWhiteSpace(entry.PropertyName))
                {
                    EditorGUILayout.HelpBox(
                        "每条标记都需要 Property Name 和 Target。Resource Location 可选；填写后会生成 EasyUIResource 特性与常量。",
                        MessageType.Error);
                    break;
                }
            }
            if (GUILayout.Button("Select Owning Display To Generate"))
            {
                EasyUIDisplay display = marker.GetComponentInParent<EasyUIDisplay>();
                if (display == null)
                    EditorUtility.DisplayDialog("Easy UI", "上级没有 EasyUIDisplay。", "确定");
                else
                    Selection.activeObject = display;
            }
        }
    }
}
