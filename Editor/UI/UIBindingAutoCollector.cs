using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    internal static class UIBindingAutoCollector
    {
        public static List<UIBindingDefinition> Collect(UIBindingContext context)
        {
            var result = new List<UIBindingDefinition>();
            var transforms = context.GetComponentsInChildren<Transform>(true);
            foreach (var current in transforms)
            {
                if (!current.name.StartsWith("bind_", StringComparison.OrdinalIgnoreCase)) continue;
                string expression = current.name.Substring(5);
                if (string.IsNullOrWhiteSpace(expression)) continue;

                UIBindingProperty? explicitProperty = null;
                int separator = expression.LastIndexOf('$');
                if (separator > 0 && Enum.TryParse(expression.Substring(separator + 1), true, out UIBindingProperty parsed))
                {
                    explicitProperty = parsed;
                    expression = expression.Substring(0, separator);
                }

                if (!TryFindTarget(current, explicitProperty, out var target, out var property, out var twoWay))
                {
                    Debug.LogWarning($"[Easy UI] {current.name} 没有与绑定属性兼容的 UGUI 组件。", current);
                    continue;
                }
                result.Add(new UIBindingDefinition(target, property, expression, "{0}", twoWay));
            }
            return result;
        }

        public static List<UIBindingDefinition> Merge(
            UIBindingContext context,
            IEnumerable<UIBindingDefinition> collected,
            out int addedCount)
        {
            var result = new List<UIBindingDefinition>();
            if (context != null) result.AddRange(context.Bindings);
            addedCount = 0;
            if (collected == null) return result;

            foreach (UIBindingDefinition candidate in collected)
            {
                if (candidate == null || Contains(result, candidate)) continue;
                result.Add(candidate);
                addedCount++;
            }
            return result;
        }

        private static bool Contains(
            List<UIBindingDefinition> bindings,
            UIBindingDefinition candidate)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                UIBindingDefinition existing = bindings[i];
                if (existing != null && existing.Target == candidate.Target &&
                    existing.TargetProperty == candidate.TargetProperty)
                    return true;
            }
            return false;
        }

        private static bool TryFindTarget(
            Transform node,
            UIBindingProperty? requested,
            out Component target,
            out UIBindingProperty property,
            out bool twoWay)
        {
            target = null;
            property = requested ?? UIBindingProperty.Text;
            twoWay = false;

            if (requested.HasValue)
            {
                property = requested.Value;
                target = FindForProperty(node, property, out twoWay);
                return target != null;
            }

            if ((target = node.GetComponent<InputField>()) != null) { property = UIBindingProperty.Text; twoWay = true; return true; }
            if ((target = FindByTypeName(node, "TMPro.TMP_InputField")) != null) { property = UIBindingProperty.Text; twoWay = true; return true; }
            if ((target = node.GetComponent<Toggle>()) != null) { property = UIBindingProperty.IsOn; twoWay = true; return true; }
            if ((target = node.GetComponent<Slider>()) != null) { property = UIBindingProperty.Value; twoWay = true; return true; }
            if ((target = node.GetComponent<Scrollbar>()) != null) { property = UIBindingProperty.Value; twoWay = true; return true; }
            if ((target = node.GetComponent<Dropdown>()) != null) { property = UIBindingProperty.Value; twoWay = true; return true; }
            if ((target = FindByTypeName(node, "TMPro.TMP_Dropdown")) != null) { property = UIBindingProperty.Value; twoWay = true; return true; }
            if ((target = node.GetComponent<Text>()) != null) { property = UIBindingProperty.Text; return true; }
            if ((target = FindByTypeName(node, "TMPro.TMP_Text")) != null) { property = UIBindingProperty.Text; return true; }
            if ((target = node.GetComponent<Button>()) != null) { property = UIBindingProperty.Interactable; return true; }
            if ((target = node.GetComponent<Image>()) != null) { property = UIBindingProperty.Sprite; return true; }
            if ((target = node.GetComponent<CanvasGroup>()) != null) { property = UIBindingProperty.Alpha; return true; }
            return false;
        }

        private static Component FindForProperty(Transform node, UIBindingProperty property, out bool twoWay)
        {
            twoWay = false;
            switch (property)
            {
                case UIBindingProperty.Text:
                    var input = node.GetComponent<InputField>();
                    if (input != null) { twoWay = true; return input; }
                    Component tmpInput = FindByTypeName(node, "TMPro.TMP_InputField");
                    if (tmpInput != null) { twoWay = true; return tmpInput; }
                    Component label = node.GetComponent<Text>();
                    return label ?? FindByTypeName(node, "TMPro.TMP_Text");
                case UIBindingProperty.Interactable:
                    return node.GetComponent<Selectable>();
                case UIBindingProperty.Value:
                    Component valueTarget = node.GetComponent<Slider>();
                    if (valueTarget == null) valueTarget = node.GetComponent<Scrollbar>();
                    if (valueTarget == null) valueTarget = node.GetComponent<Dropdown>();
                    if (valueTarget == null) valueTarget = FindByTypeName(node, "TMPro.TMP_Dropdown");
                    twoWay = valueTarget != null;
                    return valueTarget;
                case UIBindingProperty.IsOn:
                    twoWay = true;
                    return node.GetComponent<Toggle>();
                case UIBindingProperty.Color:
                    return node.GetComponent<Graphic>();
                case UIBindingProperty.Sprite:
                case UIBindingProperty.FillAmount:
                    return node.GetComponent<Image>();
                case UIBindingProperty.SpriteIndex:
                    return node.GetComponent<EasyImage>();
                case UIBindingProperty.Alpha:
                    return node.GetComponent<CanvasGroup>();
                case UIBindingProperty.Active:
                    return node.GetComponent<Component>();
                default:
                    return null;
            }
        }

        private static Component FindByTypeName(Transform node, string fullName)
        {
            foreach (Component component in node.GetComponents<Component>())
            {
                for (Type type = component == null ? null : component.GetType(); type != null; type = type.BaseType)
                    if (type.FullName == fullName) return component;
            }
            return null;
        }
    }
}
