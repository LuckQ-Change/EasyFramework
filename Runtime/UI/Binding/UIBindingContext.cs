using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Binding Context")]
    [DisallowMultipleComponent]
    public sealed class UIBindingContext : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Object _source;
        [SerializeField] private string _generatedBindingId;
        [SerializeField] private List<UIBindingDefinition> _bindings = new List<UIBindingDefinition>();
        [SerializeField] private bool _warnOnMissingBinding = true;

        private object _runtimeSource;
        private CompositeDisposable _scope;

        public object Source => _runtimeSource ?? _source;
        public string GeneratedBindingId => _generatedBindingId;
        public IReadOnlyList<UIBindingDefinition> Bindings => _bindings;

        private void OnEnable() => Bind();
        private void OnDisable() => Unbind();

        public void SetSource(object source)
        {
            if (ReferenceEquals(Source, source)) return;
            _runtimeSource = source;
            if (isActiveAndEnabled) Bind();
        }

        public void Bind()
        {
            Unbind();
            _scope = new CompositeDisposable();
            if (Source == null)
            {
                if (_warnOnMissingBinding) Log.Warn($"[UI] {name}: binding source is null.");
                return;
            }

            if (GeneratedUIBindingRegistry.TryInstall(_generatedBindingId, this)) return;
            for (int i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (binding == null) continue;
                RegisterBinding(
                    binding.Target,
                    binding.TargetProperty,
                    binding.SourceKey,
                    binding.Format,
                    binding.TwoWay);
            }
        }

        public void Unbind()
        {
            _scope?.Dispose();
            _scope = null;
        }

        public void RegisterGenerated(
            string relativePath,
            Type componentType,
            UIBindingProperty targetProperty,
            string sourceKey,
            string format,
            bool twoWay)
        {
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                Warn($"Generated target type is invalid for '{sourceKey}'.");
                return;
            }

            Transform targetTransform = string.IsNullOrEmpty(relativePath) ? transform : transform.Find(relativePath);
            if (targetTransform == null)
            {
                Warn($"Generated path '{relativePath}' was not found for '{sourceKey}'. Regenerate bindings.");
                return;
            }

            var target = targetTransform.GetComponent(componentType);
            RegisterBinding(target, targetProperty, sourceKey, format, twoWay);
        }

        public void RegisterBinding(
            Component target,
            UIBindingProperty targetProperty,
            string sourceKey,
            string format = "{0}",
            bool twoWay = false)
        {
            if (_scope == null) _scope = new CompositeDisposable();
            if (Source == null)
            {
                Warn($"Source is null for binding '{sourceKey}'.");
                return;
            }
            if (target == null)
            {
                Warn($"Target is missing for binding '{sourceKey}'.");
                return;
            }
            if (!UIBindingSourceResolver.TryResolve(Source, sourceKey, out var property))
            {
                Warn($"Source '{Source.GetType().Name}' has no reactive member named '{sourceKey}'.");
                return;
            }

            bool applyingSource = false;
            _scope.Add(property.Subscribe(value =>
            {
                applyingSource = true;
                try { ApplyValue(target, targetProperty, value, format); }
                finally { applyingSource = false; }
            }));

            if (twoWay && property is IReactiveProperty writable)
            {
                _scope.Add(SubscribeTarget(target, targetProperty, value =>
                {
                    if (!applyingSource && !writable.TrySetValue(value))
                    {
                        Warn($"Cannot convert UI value for '{sourceKey}' to {writable.ValueType.Name}.");
                    }
                }));
            }
        }

        public void ReplaceBindings(IEnumerable<UIBindingDefinition> bindings)
        {
            _bindings.Clear();
            if (bindings != null) _bindings.AddRange(bindings);
            if (isActiveAndEnabled) Bind();
        }

        public void SetGeneratedBindingId(string bindingId) => _generatedBindingId = bindingId;

        private static void ApplyValue(Component target, UIBindingProperty property, object value, string format)
        {
            switch (property)
            {
                case UIBindingProperty.Text:
                    string text = FormatValue(value, format);
                    UIComponentAdapter.TrySetText(target, text);
                    break;
                case UIBindingProperty.Interactable:
                    if (target is Selectable selectable && Try<bool>(value, out var interactable))
                        selectable.interactable = interactable;
                    break;
                case UIBindingProperty.Value:
                    if (target is Slider slider && Try<float>(value, out var sliderValue)) slider.SetValueWithoutNotify(sliderValue);
                    else if (target is Scrollbar scrollbar && Try<float>(value, out var scrollbarValue)) scrollbar.SetValueWithoutNotify(scrollbarValue);
                    else if (Try<int>(value, out var dropdownValue)) UIComponentAdapter.TrySetDropdownValue(target, dropdownValue);
                    break;
                case UIBindingProperty.IsOn:
                    if (target is Toggle toggle && Try<bool>(value, out var isOn)) toggle.SetIsOnWithoutNotify(isOn);
                    break;
                case UIBindingProperty.Color:
                    if (target is Graphic graphic && value is Color color) graphic.color = color;
                    break;
                case UIBindingProperty.Sprite:
                    if (target is Image image && (value == null || value is Sprite)) image.sprite = value as Sprite;
                    break;
                case UIBindingProperty.Alpha:
                    if (target is CanvasGroup group && Try<float>(value, out var alpha)) group.alpha = alpha;
                    break;
                case UIBindingProperty.FillAmount:
                    if (target is Image fillImage && Try<float>(value, out var fill)) fillImage.fillAmount = fill;
                    break;
                case UIBindingProperty.SpriteIndex:
                    if (target is EasyImage easyImage && Try<int>(value, out var spriteIndex))
                        easyImage.SetSpriteIndex(spriteIndex);
                    break;
                case UIBindingProperty.Active:
                    if (Try<bool>(value, out var active)) target.gameObject.SetActive(active);
                    break;
            }
        }

        private static IDisposable SubscribeTarget(Component target, UIBindingProperty property, Action<object> setter)
        {
            if (property == UIBindingProperty.Text)
                return UIComponentAdapter.SubscribeText(target, setter);
            if (property == UIBindingProperty.IsOn && target is Toggle toggle)
                return Listen(toggle.onValueChanged, value => setter(value));
            if (property == UIBindingProperty.Value && target is Slider slider)
                return Listen(slider.onValueChanged, value => setter(value));
            if (property == UIBindingProperty.Value && target is Scrollbar scrollbar)
                return Listen(scrollbar.onValueChanged, value => setter(value));
            if (property == UIBindingProperty.Value)
                return UIComponentAdapter.SubscribeDropdown(target, setter);
            return null;
        }

        private static IDisposable Listen<T>(UnityEvent<T> unityEvent, UnityAction<T> listener)
        {
            unityEvent.AddListener(listener);
            return new ActionDisposable(() => unityEvent.RemoveListener(listener));
        }

        private static bool Try<T>(object value, out T converted)
        {
            if (UIValueConverter.TryConvert(value, typeof(T), out var boxed))
            {
                converted = (T)boxed;
                return true;
            }
            converted = default(T);
            return false;
        }

        private static string FormatValue(object value, string format)
        {
            if (string.IsNullOrEmpty(format) || format == "{0}") return value?.ToString() ?? string.Empty;
            try { return string.Format(CultureInfo.CurrentCulture, format, value); }
            catch (FormatException) { return value?.ToString() ?? string.Empty; }
        }

        private void Warn(string message)
        {
            if (_warnOnMissingBinding) Log.Warn($"[UI] {name}: {message}");
        }
    }
}
