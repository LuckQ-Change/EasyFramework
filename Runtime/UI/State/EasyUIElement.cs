using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    public interface IEasyUIComponent { }

    [AddComponentMenu("EasyFramework/UI/UI Element")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class EasyUIElement : MonoBehaviour
    {
        [SerializeField] private EasyUIStateController _controller;
        [SerializeField] private bool _captureDefaultOnAwake = true;
        [SerializeField] private UIStateVariant _defaultValue = new UIStateVariant("Default");
        [SerializeField] private List<UIStateVariant> _variants = new List<UIStateVariant>();

        public EasyUIStateController Controller
        {
            get
            {
                if (_controller == null) _controller = GetComponentInParent<EasyUIStateController>(true);
                return _controller;
            }
            set => _controller = value;
        }

        public IReadOnlyList<UIStateVariant> Variants => _variants;

        private void Reset()
        {
            _controller = GetComponentInParent<EasyUIStateController>(true);
            CaptureDefault();
        }

        private void Awake()
        {
            if (_captureDefaultOnAwake || _defaultValue.Properties == UIStateProperty.None) CaptureDefault();
        }

        private void OnEnable()
        {
            var controller = Controller;
            if (controller != null) ApplyState(controller.SelectedState);
        }

        public void CaptureDefault()
        {
            var properties = UIStateProperty.Active;
            _defaultValue.SetState("Default");
            _defaultValue.SetActive(gameObject.activeSelf);

            if (TryGetComponent<Selectable>(out var selectable))
            {
                properties |= UIStateProperty.Interactable;
                _defaultValue.SetInteractable(selectable.interactable);
            }
            if (TryGetComponent<Graphic>(out var graphic))
            {
                properties |= UIStateProperty.Color;
                _defaultValue.SetColor(graphic.color);
            }
            if (TryGetComponent<CanvasGroup>(out var canvasGroup))
            {
                properties |= UIStateProperty.Alpha;
                _defaultValue.SetAlpha(canvasGroup.alpha);
            }
            if (TryGetComponent<Image>(out var image))
            {
                properties |= UIStateProperty.Sprite;
                _defaultValue.SetSprite(image.sprite);
            }
            if (TryGetComponent<EasyImage>(out var easyImage) && easyImage.SpriteIndex >= 0)
            {
                properties |= UIStateProperty.SpriteIndex;
                _defaultValue.SetSpriteIndex(easyImage.SpriteIndex);
            }
            foreach (Component component in GetComponents<Component>())
            {
                if (!UIComponentAdapter.TryGetText(component, out string text)) continue;
                properties |= UIStateProperty.Text;
                _defaultValue.SetText(text);
                break;
            }

            if (TryReadValue(out var value))
            {
                properties |= UIStateProperty.Value;
                _defaultValue.SetValue(value);
            }
            _defaultValue.SetProperties(properties);
        }

        public void ApplyState(string state)
        {
            Apply(_defaultValue);
            for (int i = 0; i < _variants.Count; i++)
            {
                var variant = _variants[i];
                if (variant != null && variant.State == state)
                {
                    Apply(variant);
                    break;
                }
            }
        }

        public void ReplaceVariants(IEnumerable<UIStateVariant> variants)
        {
            _variants.Clear();
            if (variants != null) _variants.AddRange(variants);
            var controller = Controller;
            if (controller != null) ApplyState(controller.SelectedState);
        }

        private void Apply(UIStateVariant variant)
        {
            if (variant == null) return;
            var properties = variant.Properties;
            if ((properties & UIStateProperty.Active) != 0 && variant.Active && !gameObject.activeSelf)
                gameObject.SetActive(true);
            if ((properties & UIStateProperty.Interactable) != 0 && TryGetComponent<Selectable>(out var selectable))
                selectable.interactable = variant.Interactable;
            if ((properties & UIStateProperty.Color) != 0 && TryGetComponent<Graphic>(out var graphic))
                graphic.color = variant.Color;
            if ((properties & UIStateProperty.Alpha) != 0 && TryGetComponent<CanvasGroup>(out var canvasGroup))
                canvasGroup.alpha = variant.Alpha;
            if ((properties & UIStateProperty.Sprite) != 0 && TryGetComponent<Image>(out var image))
                image.sprite = variant.Sprite;
            if ((properties & UIStateProperty.SpriteIndex) != 0 && TryGetComponent<EasyImage>(out var easyImage))
                easyImage.SetSpriteIndex(variant.SpriteIndex);
            if ((properties & UIStateProperty.Text) != 0)
            {
                foreach (Component component in GetComponents<Component>())
                    if (UIComponentAdapter.TrySetText(component, variant.Text)) break;
            }
            if ((properties & UIStateProperty.Value) != 0) ApplyValue(variant.Value);
            if ((properties & UIStateProperty.Active) != 0 && !variant.Active && gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        private bool TryReadValue(out float value)
        {
            if (TryGetComponent<Slider>(out var slider)) { value = slider.value; return true; }
            if (TryGetComponent<Scrollbar>(out var scrollbar)) { value = scrollbar.value; return true; }
            if (TryGetComponent<Dropdown>(out var dropdown)) { value = dropdown.value; return true; }
            foreach (Component component in GetComponents<Component>())
                if (UIComponentAdapter.TryReadDropdownValue(component, out value)) return true;
            if (TryGetComponent<Toggle>(out var toggle)) { value = toggle.isOn ? 1f : 0f; return true; }
            if (TryGetComponent<Image>(out var image)) { value = image.fillAmount; return true; }
            value = 0f;
            return false;
        }

        private void ApplyValue(float value)
        {
            if (TryGetComponent<Slider>(out var slider)) { slider.SetValueWithoutNotify(value); return; }
            if (TryGetComponent<Scrollbar>(out var scrollbar)) { scrollbar.SetValueWithoutNotify(value); return; }
            if (TryGetComponent<Dropdown>(out var dropdown)) { dropdown.SetValueWithoutNotify(Mathf.RoundToInt(value)); return; }
            foreach (Component component in GetComponents<Component>())
                if (UIComponentAdapter.TrySetDropdownValue(component, Mathf.RoundToInt(value))) return;
            if (TryGetComponent<Toggle>(out var toggle)) toggle.SetIsOnWithoutNotify(value > 0.5f);
            else if (TryGetComponent<Image>(out var image)) image.fillAmount = value;
        }
    }
}
