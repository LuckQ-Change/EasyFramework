using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    internal static class UIComponentAdapter
    {
        public static bool IsText(Component component) =>
            component is Text || Is(component, "TMPro.TMP_Text") || Is(component, "TMPro.TMP_InputField");

        public static bool IsInputText(Component component) =>
            component is InputField || Is(component, "TMPro.TMP_InputField");

        public static bool IsDropdown(Component component) =>
            component is Dropdown || Is(component, "TMPro.TMP_Dropdown");

        public static bool TrySetText(Component component, string value)
        {
            if (component is Text label) { label.text = value; return true; }
            if (component is InputField input) { input.SetTextWithoutNotify(value); return true; }
            if (!IsText(component)) return false;
            MethodInfo withoutNotify = component.GetType().GetMethod(
                "SetTextWithoutNotify", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string) }, null);
            if (withoutNotify != null) withoutNotify.Invoke(component, new object[] { value });
            else component.GetType().GetProperty("text")?.SetValue(component, value, null);
            return true;
        }

        public static bool TryGetText(Component component, out string value)
        {
            if (component is Text label) { value = label.text; return true; }
            if (component is InputField input) { value = input.text; return true; }
            if (IsText(component))
            {
                value = component.GetType().GetProperty("text")?.GetValue(component, null) as string;
                return true;
            }
            value = null;
            return false;
        }

        public static bool TrySetDropdownValue(Component component, int value)
        {
            if (component is Dropdown dropdown) { dropdown.SetValueWithoutNotify(value); return true; }
            if (!IsDropdown(component)) return false;
            MethodInfo method = component.GetType().GetMethod(
                "SetValueWithoutNotify", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(int) }, null);
            if (method == null) return false;
            method.Invoke(component, new object[] { value });
            return true;
        }

        public static bool TryReadDropdownValue(Component component, out float value)
        {
            if (component is Dropdown dropdown) { value = dropdown.value; return true; }
            if (IsDropdown(component))
            {
                object raw = component.GetType().GetProperty("value")?.GetValue(component, null);
                if (raw is int integer) { value = integer; return true; }
            }
            value = 0f;
            return false;
        }

        public static IDisposable SubscribeText(Component component, Action<object> setter)
        {
            if (component is InputField input)
                return Listen(input.onValueChanged, value => setter(value));
            if (!Is(component, "TMPro.TMP_InputField")) return null;
            object eventObject = component.GetType().GetProperty("onValueChanged")?.GetValue(component, null);
            return eventObject is UnityEvent<string> unityEvent
                ? Listen(unityEvent, value => setter(value))
                : null;
        }

        public static IDisposable SubscribeDropdown(Component component, Action<object> setter)
        {
            if (component is Dropdown dropdown)
                return Listen(dropdown.onValueChanged, value => setter(value));
            if (!IsDropdown(component)) return null;
            object eventObject = component.GetType().GetProperty("onValueChanged")?.GetValue(component, null);
            return eventObject is UnityEvent<int> unityEvent
                ? Listen(unityEvent, value => setter(value))
                : null;
        }

        private static bool Is(Component component, string expectedType)
        {
            for (Type type = component == null ? null : component.GetType(); type != null; type = type.BaseType)
                if (type.FullName == expectedType) return true;
            return false;
        }

        private static IDisposable Listen<T>(UnityEvent<T> unityEvent, UnityAction<T> listener)
        {
            unityEvent.AddListener(listener);
            return new ActionDisposable(() => unityEvent.RemoveListener(listener));
        }
    }
}
