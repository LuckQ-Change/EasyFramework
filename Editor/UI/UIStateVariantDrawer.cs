using EasyFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    [CustomPropertyDrawer(typeof(UIStateVariant))]
    internal sealed class UIStateVariantDrawer : PropertyDrawer
    {
        private static readonly UIStateProperty[] OrderedProperties =
        {
            UIStateProperty.Active,
            UIStateProperty.Interactable,
            UIStateProperty.Color,
            UIStateProperty.Alpha,
            UIStateProperty.Sprite,
            UIStateProperty.SpriteIndex,
            UIStateProperty.Text,
            UIStateProperty.Value,
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
            bool isDefault = property.name == "_defaultValue";
            int rows = (isDefault ? 1 : 2) + CountProperties(GetSupportedProperties(property));
            return rows * EditorGUIUtility.singleLineHeight + (rows - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);
            bool isDefault = property.name == "_defaultValue";

            var state = property.FindPropertyRelative("_state");
            string title = isDefault
                ? "默认外观"
                : (string.IsNullOrWhiteSpace(state.stringValue) ? label.text : state.stringValue);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, title, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            if (!isDefault)
            {
                line.y += lineHeight + spacing;
                DrawStatePopup(line, property, state);
            }

            UIStateProperty supported = GetSupportedProperties(property);
            foreach (UIStateProperty stateProperty in OrderedProperties)
            {
                if ((supported & stateProperty) == 0) continue;
                line.y += lineHeight + spacing;
                DrawPropertyRow(line, property, stateProperty);
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static void DrawPropertyRow(Rect rect, SerializedProperty variant, UIStateProperty stateProperty)
        {
            var flags = variant.FindPropertyRelative("_properties");
            int current = flags.intValue;
            bool enabled = (current & (int)stateProperty) != 0;

            float labelWidth = Mathf.Min(130f, rect.width * 0.42f);
            var toggleRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            var valueRect = new Rect(rect.x + labelWidth + 4f, rect.y, rect.width - labelWidth - 4f, rect.height);
            bool next = EditorGUI.ToggleLeft(toggleRect, GetLabel(variant, stateProperty), enabled);
            if (next != enabled)
            {
                flags.intValue = next ? current | (int)stateProperty : current & ~(int)stateProperty;
                enabled = next;
            }

            using (new EditorGUI.DisabledScope(!enabled))
            {
                string fieldName = GetFieldName(stateProperty);
                var value = variant.FindPropertyRelative(fieldName);
                if (stateProperty == UIStateProperty.Text)
                    value.stringValue = EditorGUI.TextField(valueRect, value.stringValue);
                else
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
            }
        }

        private static void DrawStatePopup(Rect rect, SerializedProperty variant, SerializedProperty state)
        {
            if (!(variant.serializedObject.targetObject is UIElement element) || element.Controller == null ||
                element.Controller.States.Count == 0)
            {
                EditorGUI.PropertyField(rect, state, new GUIContent("State"));
                return;
            }

            EditorGUI.BeginChangeCheck();
            var states = element.Controller.States;
            var labels = new string[states.Count];
            int selected = -1;
            for (int i = 0; i < states.Count; i++)
            {
                labels[i] = states[i];
                if (states[i] == state.stringValue) selected = i;
            }
            if (selected < 0)
            {
                EditorGUI.LabelField(rect, "State", state.stringValue);
                return;
            }
            selected = EditorGUI.Popup(rect, "State", selected, labels);
            if (EditorGUI.EndChangeCheck())
                state.stringValue = states[Mathf.Clamp(selected, 0, states.Count - 1)];
        }

        private static UIStateProperty GetSupportedProperties(SerializedProperty property)
        {
            if (!(property.serializedObject.targetObject is UIElement element)) return UIStateProperty.All;

            UIStateProperty result = UIStateProperty.Active;
            if (element.TryGetComponent<Selectable>(out _)) result |= UIStateProperty.Interactable;
            if (element.TryGetComponent<Graphic>(out _)) result |= UIStateProperty.Color;
            if (element.TryGetComponent<CanvasGroup>(out _)) result |= UIStateProperty.Alpha;
            if (element.TryGetComponent<Image>(out _)) result |= UIStateProperty.Sprite | UIStateProperty.Value;
            if (element.TryGetComponent<UIImage>(out _)) result |= UIStateProperty.SpriteIndex;
            if (element.TryGetComponent<Text>(out _) || element.TryGetComponent<InputField>(out _) ||
                HasComponent(element, "TMPro.TMP_Text") || HasComponent(element, "TMPro.TMP_InputField"))
                result |= UIStateProperty.Text;
            if (element.TryGetComponent<Slider>(out _) || element.TryGetComponent<Scrollbar>(out _) ||
                element.TryGetComponent<Dropdown>(out _) || element.TryGetComponent<Toggle>(out _) ||
                HasComponent(element, "TMPro.TMP_Dropdown"))
                result |= UIStateProperty.Value;
            return result;
        }

        private static bool HasComponent(UIElement element, string fullName)
        {
            foreach (Component component in element.GetComponents<Component>())
            {
                for (System.Type type = component == null ? null : component.GetType(); type != null; type = type.BaseType)
                    if (type.FullName == fullName) return true;
            }
            return false;
        }

        private static int CountProperties(UIStateProperty properties)
        {
            int count = 0;
            foreach (UIStateProperty value in OrderedProperties)
                if ((properties & value) != 0) count++;
            return count;
        }

        private static string GetFieldName(UIStateProperty property)
        {
            switch (property)
            {
                case UIStateProperty.Active: return "_active";
                case UIStateProperty.Interactable: return "_interactable";
                case UIStateProperty.Color: return "_color";
                case UIStateProperty.Alpha: return "_alpha";
                case UIStateProperty.Sprite: return "_sprite";
                case UIStateProperty.SpriteIndex: return "_spriteIndex";
                case UIStateProperty.Text: return "_text";
                case UIStateProperty.Value: return "_value";
                default: return "_value";
            }
        }

        private static string GetLabel(SerializedProperty variant, UIStateProperty property)
        {
            if (property == UIStateProperty.Value &&
                variant.serializedObject.targetObject is UIElement element &&
                element.TryGetComponent<Image>(out _))
                return "Fill Amount";
            if (property == UIStateProperty.SpriteIndex) return "Sprite Index";
            return ObjectNames.NicifyVariableName(property.ToString());
        }
    }
}
