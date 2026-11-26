using System;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    /// <summary>编辑页面转场的时间轴覆盖和共享预设。</summary>
    [CustomEditor(typeof(UITransition))]
    [CanEditMultipleObjects]
    internal sealed class UITransitionInspector : UnityEditor.Editor
    {
        private SerializedProperty _preset;
        private SerializedProperty _openTimeline;
        private SerializedProperty _openAnimation;
        private SerializedProperty _closeTimeline;
        private SerializedProperty _closeAnimation;

        private void OnEnable()
        {
            _preset = serializedObject.FindProperty("_preset");
            _openTimeline = serializedObject.FindProperty("_openTimeline");
            _openAnimation = serializedObject.FindProperty("_openAnimation");
            _closeTimeline = serializedObject.FindProperty("_closeTimeline");
            _closeAnimation = serializedObject.FindProperty("_closeAnimation");
        }

        /// <summary>绘制进退场配置和共享转场预设。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            // 同一轮绘制使用编辑前的配置，切换引用后由下一轮更新对应字段。
            bool hasOpenTimeline = HasTimeline(_openTimeline, _openAnimation);
            bool hasCloseTimeline = HasTimeline(_closeTimeline, _closeAnimation);

            EditorGUILayout.PropertyField(_preset, new GUIContent("共享转场预设", "优先使用自定义时间轴，其次使用共享预设，留空时应用标准淡入淡出。"));
            EditorGUILayout.Space();
            DrawTimeline("进场", _openTimeline, _openAnimation, hasOpenTimeline);
            DrawTimeline("退场", _closeTimeline, _closeAnimation, hasCloseTimeline);

            if (!serializedObject.isEditingMultipleObjects) DrawValidation();
            serializedObject.ApplyModifiedProperties();
        }

        private static bool HasTimeline(SerializedProperty timeline, SerializedProperty animation)
        {
            return timeline.hasMultipleDifferentValues || timeline.objectReferenceValue != null ||
                animation.hasMultipleDifferentValues || !string.IsNullOrEmpty(animation.stringValue);
        }

        private static void DrawTimeline(string title, SerializedProperty timeline, SerializedProperty animation, bool hasTimeline)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(timeline, new GUIContent("自定义时间轴"));
            if (hasTimeline) EditorGUILayout.PropertyField(animation, new GUIContent("动画名称"));
            EditorGUILayout.Space();
        }

        private void DrawValidation()
        {
            try
            {
                ((UITransition)target).ValidateConfiguration();
            }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }

    }
}
