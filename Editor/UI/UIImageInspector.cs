using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    [CustomEditor(typeof(UIImage))]
    internal sealed class UIImageInspector : ImageEditor
    {
        private SerializedProperty _sprites;
        private SerializedProperty _spriteIndex;
        private SerializedProperty _applySpriteIndexOnEnable;

        protected override void OnEnable()
        {
            base.OnEnable();
            _sprites = serializedObject.FindProperty("_sprites");
            _spriteIndex = serializedObject.FindProperty("_spriteIndex");
            _applySpriteIndexOnEnable = serializedObject.FindProperty("_applySpriteIndexOnEnable");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UIImage Sprite Library", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_sprites, new GUIContent("Sprites"), true);
            EditorGUILayout.PropertyField(_applySpriteIndexOnEnable, new GUIContent("Apply Index On Enable"));

            int count = _sprites.arraySize;
            if (count == 0)
            {
                _spriteIndex.intValue = -1;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("Sprite Index", 0, new[] { "No sprites" });
            }
            else
            {
                var options = new List<string> { "-1  Keep Source Image" };
                for (int i = 0; i < count; i++)
                {
                    var sprite = _sprites.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                    options.Add($"{i}  {(sprite == null ? "None" : sprite.name)}");
                }
                int popupIndex = Mathf.Clamp(_spriteIndex.intValue + 1, 0, count);
                _spriteIndex.intValue = EditorGUILayout.Popup("Sprite Index", popupIndex, options.ToArray()) - 1;
            }

            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                var image = (UIImage)target;
                Undo.RecordObject(image, "Change UIImage sprite library");
                image.ApplySpriteIndex();
                EditorUtility.SetDirty(image);
            }

            using (new EditorGUI.DisabledScope(count == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous")) ChangeIndex(false);
                if (GUILayout.Button("Next")) ChangeIndex(true);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ChangeIndex(bool next)
        {
            var image = (UIImage)target;
            Undo.RecordObject(image, next ? "Next UIImage sprite" : "Previous UIImage sprite");
            if (next) image.NextSprite();
            else image.PreviousSprite();
            EditorUtility.SetDirty(image);
            serializedObject.Update();
        }
    }
}
