using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(EasyUIRuntimeHost))]
    internal sealed class EasyUIManagerInspector : UnityEditor.Editor
    {
        [MenuItem("GameObject/EasyFramework/UI Manager", false, 10)]
        private static void CreateManager(MenuCommand command)
        {
            var managerObject = new GameObject(
                "[EasyUI]",
                typeof(RectTransform),
                typeof(EasyUIRuntimeHost));
            Undo.RegisterCreatedObjectUndo(managerObject, "Create Easy UI Manager");
            GameObjectUtility.SetParentAndAlign(managerObject, command.context as GameObject);
            Selection.activeGameObject = managerObject;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var host = (EasyUIRuntimeHost)target;
            var manager = host.Manager;
            if (manager == null)
            {
                EditorGUILayout.HelpBox(
                    "应用退出期间无法使用 UI Manager。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UI 层级", EditorStyles.boldLabel);
            foreach (UILayer layer in System.Enum.GetValues(typeof(UILayer)))
            {
                RectTransform root = manager.GetLayerRoot(layer);
                EditorGUILayout.LabelField(
                    layer.ToString(),
                    root == null ? "缺失" : $"排序值 {(int)layer} · {root.childCount} 个界面");
            }

            if (!Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"已打开界面（{manager.OpenCount}）", EditorStyles.boldLabel);
            for (int i = manager.OpenDisplays.Count - 1; i >= 0; i--)
            {
                EasyUIDisplay display = manager.OpenDisplays[i];
                if (display != null)
                    EditorGUILayout.ObjectField(display.CurrentLayer.ToString(), display, typeof(EasyUIDisplay), true);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("返回 / 关闭顶层")) manager.Back();
            if (GUILayout.Button("关闭全部")) manager.CloseAll();
            EditorGUILayout.EndHorizontal();
        }
    }
}
