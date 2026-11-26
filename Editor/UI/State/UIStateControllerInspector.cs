using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>显示状态控制器的初态、运行状态和工作台入口。</summary>
    [CustomEditor(typeof(UIStateController))]
    internal sealed class UIStateControllerInspector : UnityEditor.Editor
    {
        private SerializedProperty _onStateChanged;

        private void OnEnable()
        {
            _onStateChanged = serializedObject.FindProperty("_onStateChanged");
        }

        /// <summary>绘制初态配置、运行时事件与工作台入口。</summary>
        public override void OnInspectorGUI()
        {
            if (target is not UIStateController controller || controller == null) return;
            if (UIStatePreview.IsPreviewObject(controller.gameObject))
            {
                EditorGUILayout.HelpBox("这是预览副本。请在状态工作台记录画布上的修改。", MessageType.Info);
                return;
            }

            bool isPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
            bool canSetInitial = !isPlaying && !EditorUtility.IsPersistent(controller);
            var states = controller.States;
            var labels = new string[states.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = states[i];

            using (new EditorGUI.DisabledScope(!canSetInitial))
            {
                int previous = controller.InitialStateIndex;
                int initialIndex = EditorGUILayout.Popup("运行时初态", previous, labels);
                if (initialIndex != previous && initialIndex >= 0 && initialIndex < labels.Length)
                {
                    Undo.RecordObject(controller, "设置 UIState 初态");
                    controller.SetInitialState(initialIndex);
                    if (PrefabUtility.IsPartOfPrefabInstance(controller))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
                    EditorUtility.SetDirty(controller);
                }
            }
            if (isPlaying) EditorGUILayout.LabelField("当前运行状态", controller.SelectedState);
            if (GUILayout.Button("打开 UIState 工作台")) UIStateWorkbench.Open(controller);

            serializedObject.Update();
            EditorGUILayout.PropertyField(_onStateChanged, new GUIContent("状态变化事件"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
