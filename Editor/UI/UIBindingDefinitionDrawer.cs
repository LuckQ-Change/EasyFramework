using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    [CustomPropertyDrawer(typeof(UIBindingDefinition))]
    internal sealed class UIBindingDefinitionDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
            return 6f * EditorGUIUtility.singleLineHeight + 5f * EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, BuildTitle(property), true);
            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;
            SerializedProperty target = property.FindPropertyRelative("_target");
            SerializedProperty targetProperty = property.FindPropertyRelative("_targetProperty");
            SerializedProperty sourceKey = property.FindPropertyRelative("_sourceKey");
            SerializedProperty format = property.FindPropertyRelative("_format");
            SerializedProperty twoWay = property.FindPropertyRelative("_twoWay");

            Next(ref line, lineHeight, spacing);
            EditorGUI.PropertyField(line, target);
            Component component = target.objectReferenceValue as Component;
            Next(ref line, lineHeight, spacing);
            DrawTargetProperty(line, targetProperty, component);
            Next(ref line, lineHeight, spacing);
            DrawSourceKey(line, property, sourceKey);
            Next(ref line, lineHeight, spacing);
            EditorGUI.PropertyField(line, format);
            Next(ref line, lineHeight, spacing);
            using (new EditorGUI.DisabledScope(!SupportsTwoWay(component, (UIBindingProperty)targetProperty.enumValueIndex)))
                EditorGUI.PropertyField(line, twoWay);
            Next(ref line, lineHeight, spacing);
            string problem = GetProblem(component, (UIBindingProperty)targetProperty.enumValueIndex, sourceKey.stringValue);
            EditorGUI.LabelField(line, string.IsNullOrEmpty(problem) ? "✓ Valid" : "⚠ " + problem,
                string.IsNullOrEmpty(problem) ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel--;
        }

        private static void DrawTargetProperty(Rect rect, SerializedProperty property, Component component)
        {
            UIBindingProperty[] supported = GetSupported(component);
            var labels = new GUIContent[supported.Length];
            int selected = 0;
            var current = (UIBindingProperty)property.enumValueIndex;
            for (int i = 0; i < supported.Length; i++)
            {
                labels[i] = new GUIContent(ObjectNames.NicifyVariableName(supported[i].ToString()));
                if (supported[i] == current) selected = i;
            }
            int next = EditorGUI.Popup(rect, new GUIContent("Target Property"), selected, labels);
            property.enumValueIndex = (int)supported[Mathf.Clamp(next, 0, supported.Length - 1)];
        }

        private static void DrawSourceKey(Rect rect, SerializedProperty binding, SerializedProperty sourceKey)
        {
            if (!(binding.serializedObject.targetObject is UIBindingContext context))
            {
                EditorGUI.PropertyField(rect, sourceKey);
                return;
            }
            Type sourceType = UIBindingEditorReflection.ResolveSourceType(context);
            string[] members = UIBindingEditorReflection.GetBindableMembers(sourceType);
            if (members.Length == 0)
            {
                EditorGUI.PropertyField(rect, sourceKey);
                return;
            }
            var choices = new string[members.Length + 1];
            choices[0] = "<Custom>";
            Array.Copy(members, 0, choices, 1, members.Length);
            int index = Array.IndexOf(members, sourceKey.stringValue);
            float popupWidth = rect.width * 0.55f;
            var popupRect = new Rect(rect.x, rect.y, popupWidth, rect.height);
            var textRect = new Rect(rect.x + popupWidth + 4f, rect.y, rect.width - popupWidth - 4f, rect.height);
            int next = EditorGUI.Popup(popupRect, "Source", index + 1, choices);
            if (next > 0) sourceKey.stringValue = members[next - 1];
            sourceKey.stringValue = EditorGUI.TextField(textRect, sourceKey.stringValue);
        }

        private static UIBindingProperty[] GetSupported(Component target)
        {
            var result = new List<UIBindingProperty> { UIBindingProperty.Active };
            if (target == null) return result.ToArray();
            if (target is Text || target is InputField || HasType(target, "TMPro.TMP_Text") || HasType(target, "TMPro.TMP_InputField"))
                result.Add(UIBindingProperty.Text);
            if (target is Selectable) result.Add(UIBindingProperty.Interactable);
            if (target is Slider || target is Scrollbar || target is Dropdown || HasType(target, "TMPro.TMP_Dropdown"))
                result.Add(UIBindingProperty.Value);
            if (target is Toggle) result.Add(UIBindingProperty.IsOn);
            if (target is Graphic) result.Add(UIBindingProperty.Color);
            if (target is Image)
            {
                result.Add(UIBindingProperty.Sprite);
                result.Add(UIBindingProperty.FillAmount);
            }
            if (target is EasyImage) result.Add(UIBindingProperty.SpriteIndex);
            if (target is CanvasGroup) result.Add(UIBindingProperty.Alpha);
            return result.ToArray();
        }

        private static bool SupportsTwoWay(Component target, UIBindingProperty property) =>
            (property == UIBindingProperty.Text && (target is InputField || HasType(target, "TMPro.TMP_InputField"))) ||
            (property == UIBindingProperty.Value && (target is Slider || target is Scrollbar || target is Dropdown || HasType(target, "TMPro.TMP_Dropdown"))) ||
            (property == UIBindingProperty.IsOn && target is Toggle);

        private static string GetProblem(Component component, UIBindingProperty property, string sourceKey)
        {
            if (component == null) return "Target is missing";
            if (string.IsNullOrWhiteSpace(sourceKey)) return "Source is empty";
            if (Array.IndexOf(GetSupported(component), property) < 0) return "Property is incompatible with target";
            return null;
        }

        private static bool HasType(Component component, string fullName)
        {
            for (Type type = component == null ? null : component.GetType(); type != null; type = type.BaseType)
                if (type.FullName == fullName) return true;
            return false;
        }

        private static string BuildTitle(SerializedProperty property)
        {
            Component target = property.FindPropertyRelative("_target").objectReferenceValue as Component;
            string source = property.FindPropertyRelative("_sourceKey").stringValue;
            return $"{(target == null ? "Missing" : target.name)} ← {(string.IsNullOrEmpty(source) ? "Source" : source)}";
        }

        private static void Next(ref Rect line, float height, float spacing) => line.y += height + spacing;
    }
}
