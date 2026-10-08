using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    public interface IUIComponent { }

    [AddComponentMenu("EasyFramework/UI/UI Element")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class UIElement : MonoBehaviour
    {
        [SerializeField] private UIStateController _controller;
        [SerializeField] private bool _captureDefaultOnAwake = true;
        [SerializeField] private UIStateVariant _defaultValue = new UIStateVariant("Default");
        [SerializeField] private List<UIStateVariant> _variants = new List<UIStateVariant>();

        public UIStateController Controller
        {
            get
            {
                if (_controller == null) _controller = GetComponentInParent<UIStateController>(true);
                return _controller;
            }
            set
            {
                _controller = value;
                SyncVariantsToController();
            }
        }

        public UIStateVariant DefaultValue => _defaultValue;
        public IReadOnlyList<UIStateVariant> Variants => _variants;

        public bool BelongsTo(UIStateController controller)
        {
            if (controller == null) return false;
            if (_controller != null) return _controller == controller;
            return GetComponentInParent<UIStateController>(true) == controller;
        }

        private void Reset()
        {
            _controller = GetComponentInParent<UIStateController>(true);
            CaptureDefault();
            SyncVariantsToController();
        }

        private void Awake()
        {
            if (_captureDefaultOnAwake || _defaultValue.Properties == UIStateProperty.None) CaptureDefault();
        }

        private void OnEnable()
        {
            SyncVariantsToController();
            var controller = Controller;
            if (controller != null) ApplyState(controller.SelectedState);
        }

        public void CaptureDefault()
        {
            CaptureInto(_defaultValue);
            _defaultValue.SetState("Default");
        }

        public bool CaptureVariant(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            UIStateVariant variant = GetOrCreateVariant(state);
            CaptureInto(variant);
            variant.SetState(state);
            return true;
        }

        public UIStateVariant FindVariant(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return null;
            for (int i = 0; i < _variants.Count; i++)
            {
                UIStateVariant variant = _variants[i];
                if (variant != null && variant.State == state) return variant;
            }
            return null;
        }

        public void SyncVariantsToController()
        {
            var controller = Controller;
            if (controller == null) return;

            IReadOnlyList<string> states = controller.States;
            var next = new List<UIStateVariant>(states.Count);
            for (int s = 0; s < states.Count; s++)
            {
                string state = states[s];
                if (string.IsNullOrWhiteSpace(state)) continue;
                UIStateVariant match = FindVariant(state);
                next.Add(match ?? new UIStateVariant(state));
            }

            if (controller.AllowUndefinedState)
            {
                for (int i = 0; i < _variants.Count; i++)
                {
                    UIStateVariant variant = _variants[i];
                    if (variant == null || string.IsNullOrWhiteSpace(variant.State)) continue;
                    if (FindInList(next, variant.State) == null) next.Add(variant);
                }
            }

            if (AreSameVariants(_variants, next)) return;
            _variants.Clear();
            _variants.AddRange(next);
        }

        public void ApplyState(string state)
        {
            Apply(_defaultValue);
            UIStateVariant variant = FindVariant(state);
            if (variant != null) Apply(variant);
        }

        public void ApplyDefaultOnly() => Apply(_defaultValue);

        public void ReplaceVariants(IEnumerable<UIStateVariant> variants)
        {
            _variants.Clear();
            if (variants != null) _variants.AddRange(variants);
            SyncVariantsToController();
            var controller = Controller;
            if (controller != null) ApplyState(controller.SelectedState);
        }

        private UIStateVariant GetOrCreateVariant(string state)
        {
            UIStateVariant variant = FindVariant(state);
            if (variant != null) return variant;
            variant = new UIStateVariant(state);
            _variants.Add(variant);
            return variant;
        }

        private void CaptureInto(UIStateVariant target)
        {
            var properties = UIStateProperty.Active;
            target.SetActive(gameObject.activeSelf);

            if (TryGetComponent<Selectable>(out var selectable))
            {
                properties |= UIStateProperty.Interactable;
                target.SetInteractable(selectable.interactable);
            }
            if (TryGetComponent<Graphic>(out var graphic))
            {
                properties |= UIStateProperty.Color;
                target.SetColor(graphic.color);
            }
            if (TryGetComponent<CanvasGroup>(out var canvasGroup))
            {
                properties |= UIStateProperty.Alpha;
                target.SetAlpha(canvasGroup.alpha);
            }
            if (TryGetComponent<Image>(out var image))
            {
                properties |= UIStateProperty.Sprite;
                target.SetSprite(image.sprite);
            }
            if (TryGetComponent<UIImage>(out var uiImage) && uiImage.SpriteIndex >= 0)
            {
                properties |= UIStateProperty.SpriteIndex;
                target.SetSpriteIndex(uiImage.SpriteIndex);
            }
            foreach (Component component in GetComponents<Component>())
            {
                if (!UIComponentAdapter.TryGetText(component, out string text)) continue;
                properties |= UIStateProperty.Text;
                target.SetText(text);
                break;
            }

            if (TryReadValue(out var value))
            {
                properties |= UIStateProperty.Value;
                target.SetValue(value);
            }
            target.SetProperties(properties);
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
            if ((properties & UIStateProperty.SpriteIndex) != 0 && TryGetComponent<UIImage>(out var uiImage))
                uiImage.SetSpriteIndex(variant.SpriteIndex);
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

        private static UIStateVariant FindInList(List<UIStateVariant> variants, string state)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                UIStateVariant variant = variants[i];
                if (variant != null && variant.State == state) return variant;
            }
            return null;
        }

        private static bool AreSameVariants(List<UIStateVariant> current, List<UIStateVariant> next)
        {
            if (current.Count != next.Count) return false;
            for (int i = 0; i < current.Count; i++)
            {
                UIStateVariant left = current[i];
                UIStateVariant right = next[i];
                if (left != right) return false;
            }
            return true;
        }
    }
}
