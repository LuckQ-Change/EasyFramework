using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    /// <summary>缓存受控节点的组件引用，并将状态值写到对应控件。</summary>
    internal sealed class UIStateTargetAccess
    {
        private readonly GameObject _target;
        private readonly Selectable _selectable;
        private readonly Graphic _graphic;
        private readonly CanvasGroup _canvasGroup;
        private readonly Image _image;
        private readonly UIImage _indexedImage;
        private readonly Component _text;
        private readonly Component _value;

        internal UIStateTargetAccess(GameObject target)
        {
            _target = target;
            _selectable = target.GetComponent<Selectable>();
            _graphic = target.GetComponent<Graphic>();
            _canvasGroup = target.GetComponent<CanvasGroup>();
            _image = target.GetComponent<Image>();
            _indexedImage = target.GetComponent<UIImage>();

            Component dropdown = null;
            foreach (Component component in target.GetComponents<Component>())
            {
                if (_text == null && (component is InputField || UIStateControlAccess.IsText(component))) _text = component;
                if (dropdown == null && UIStateControlAccess.IsDropdown(component)) dropdown = component;
            }

            Component value = target.GetComponent<Slider>();
            if (value == null) value = target.GetComponent<Scrollbar>();
            if (value == null) value = (Component)target.GetComponent<Dropdown>() ?? dropdown;
            if (value == null) value = target.GetComponent<Toggle>();
            if (value == null) value = _image;
            _value = value;
        }

        internal UIStateVariant Capture()
        {
            var values = new UIStateVariant();
            var fields = UIStateProperty.Active;
            values.SetActive(_target.activeSelf);
            if (_selectable != null)
            {
                fields |= UIStateProperty.Interactable;
                values.SetInteractable(_selectable.interactable);
            }
            if (_graphic != null)
            {
                fields |= UIStateProperty.Color;
                values.SetColor(_graphic.color);
            }
            if (_canvasGroup != null)
            {
                fields |= UIStateProperty.Alpha;
                values.SetAlpha(_canvasGroup.alpha);
            }
            if (_image != null)
            {
                fields |= UIStateProperty.Sprite;
                values.SetSprite(_image.sprite);
            }
            if (_indexedImage != null && _indexedImage.SpriteCount > 0)
            {
                fields |= UIStateProperty.SpriteIndex;
                values.SetSpriteIndex(_indexedImage.SpriteIndex);
            }
            if (_text != null && UIStateControlAccess.TryGetText(_text, out string text))
            {
                fields |= UIStateProperty.Text;
                values.SetText(text);
            }
            if (TryGetValue(out float value))
            {
                fields |= UIStateProperty.Value;
                values.SetValue(value);
            }
            values.SetProperties(fields);
            return values;
        }

        internal Component GetComponent(UIStateProperty field) => field switch
        {
            UIStateProperty.Interactable => _selectable,
            UIStateProperty.Color => _graphic,
            UIStateProperty.Alpha => _canvasGroup,
            UIStateProperty.Sprite => _image,
            UIStateProperty.Text => _text,
            UIStateProperty.Value => _value,
            UIStateProperty.SpriteIndex => _indexedImage,
            _ => null,
        };

        internal void ApplyInactive(UIStateVariant baseline, UIStateVariant state)
        {
            if (_target == null || baseline == null || !_target.activeSelf) return;
            UIStateVariant active = Resolve(baseline, state, UIStateProperty.Active);
            if ((active.Properties & UIStateProperty.Active) != 0 && !active.Active) _target.SetActive(false);
        }

        internal void ApplyFields(UIStateVariant baseline, UIStateVariant state)
        {
            if (_target == null || baseline == null) return;
            UIStateProperty fields = baseline.Properties | (state?.Properties ?? UIStateProperty.None);

            // 每个字段先取最终值，在所有节点的内容就绪后才统一启用。
            if ((fields & UIStateProperty.Interactable) != 0 && _selectable != null)
            {
                bool isInteractable = Resolve(baseline, state, UIStateProperty.Interactable).Interactable;
                if (_selectable.interactable != isInteractable) _selectable.interactable = isInteractable;
            }
            if ((fields & UIStateProperty.Color) != 0 && _graphic != null)
            {
                Color color = Resolve(baseline, state, UIStateProperty.Color).Color;
                if (_graphic.color != color) _graphic.color = color;
            }
            if ((fields & UIStateProperty.Alpha) != 0 && _canvasGroup != null)
            {
                float alpha = Resolve(baseline, state, UIStateProperty.Alpha).Alpha;
                if (!Mathf.Approximately(_canvasGroup.alpha, alpha)) _canvasGroup.alpha = alpha;
            }
            ApplySprite(fields, baseline, state);
            if ((fields & UIStateProperty.Text) != 0 && _text != null)
            {
                string text = Resolve(baseline, state, UIStateProperty.Text).Text;
                if (UIStateControlAccess.TryGetText(_text, out string current) && current != text)
                    UIStateControlAccess.TrySetText(_text, text);
            }
            if ((fields & UIStateProperty.Value) != 0 && TryGetValue(out float currentValue))
            {
                float value = Resolve(baseline, state, UIStateProperty.Value).Value;
                if (!Mathf.Approximately(currentValue, value)) SetValue(value);
            }
        }

        internal bool ApplyActive(UIStateVariant baseline, UIStateVariant state)
        {
            if (_target == null || baseline == null || _target.activeSelf) return false;
            UIStateVariant active = Resolve(baseline, state, UIStateProperty.Active);
            if ((active.Properties & UIStateProperty.Active) == 0 || !active.Active) return false;
            _target.SetActive(true);
            return true;
        }

        internal void ReapplySprite(UIStateVariant baseline, UIStateVariant state)
        {
            if (_target == null || baseline == null) return;
            ApplySprite(baseline.Properties | (state?.Properties ?? UIStateProperty.None), baseline, state);
        }

        internal void Restore(UIStateVariant values)
        {
            ApplyInactive(values, null);
            ApplyFields(values, null);
            ApplyActive(values, null);
            // 快照中的图像与索引都是原值，显式图像不能被索引恢复覆盖。
            if (_image != null && (values.Properties & UIStateProperty.Sprite) != 0 && _image.sprite != values.Sprite)
                _image.sprite = values.Sprite;
        }

        #region 私有逻辑

        private static UIStateVariant Resolve(UIStateVariant baseline, UIStateVariant state, UIStateProperty field) =>
            state != null && (state.Properties & field) != 0 ? state : baseline;

        private void ApplySprite(UIStateProperty fields, UIStateVariant baseline, UIStateVariant state)
        {
            bool hasBaselineIndex = false;
            bool hasStateIndex = false;
            if ((fields & UIStateProperty.SpriteIndex) != 0 && _indexedImage != null)
            {
                hasBaselineIndex = HasSpriteIndex(baseline);
                hasStateIndex = HasSpriteIndex(state);
                bool hasSelectedIndex = hasStateIndex || hasBaselineIndex;
                int index = hasStateIndex ? state.SpriteIndex : baseline.SpriteIndex;
                if (hasSelectedIndex && (_indexedImage.SpriteIndex != index ||
                    (_indexedImage != _image && index >= 0 && _indexedImage.sprite != _indexedImage.Sprites[index])))
                {
                    _indexedImage.SetSpriteIndex(index);
                }
            }
            if (_image == null) return;

            // 沿用旧节点先应用基准、再应用状态的优先级。单层同时声明时索引优先。
            bool isSameImage = _image == _indexedImage;
            Sprite sprite;
            if (isSameImage && hasStateIndex && state.SpriteIndex >= 0) sprite = _indexedImage.Sprites[state.SpriteIndex];
            else if (state != null && (state.Properties & UIStateProperty.Sprite) != 0) sprite = state.Sprite;
            else if (isSameImage && hasBaselineIndex && baseline.SpriteIndex >= 0) sprite = _indexedImage.Sprites[baseline.SpriteIndex];
            else if ((baseline.Properties & UIStateProperty.Sprite) != 0) sprite = baseline.Sprite;
            else return;
            if (_image.sprite != sprite) _image.sprite = sprite;
        }

        private bool HasSpriteIndex(UIStateVariant value) => value != null &&
            (value.Properties & UIStateProperty.SpriteIndex) != 0 &&
            value.SpriteIndex >= -1 && value.SpriteIndex < _indexedImage.SpriteCount;

        private bool TryGetValue(out float value)
        {
            if (_value == null)
            {
                value = 0f;
                return false;
            }
            switch (_value)
            {
                case Slider slider: value = slider.value; return true;
                case Scrollbar scrollbar: value = scrollbar.value; return true;
                case Toggle toggle: value = toggle.isOn ? 1f : 0f; return true;
                case Image image: value = image.fillAmount; return true;
                default: return UIStateControlAccess.TryReadDropdownValue(_value, out value);
            }
        }

        private void SetValue(float value)
        {
            switch (_value)
            {
                case Slider slider: slider.SetValueWithoutNotify(value); break;
                case Scrollbar scrollbar: scrollbar.SetValueWithoutNotify(value); break;
                case Toggle toggle: toggle.SetIsOnWithoutNotify(value > 0.5f); break;
                case Image image: image.fillAmount = value; break;
                default: UIStateControlAccess.TrySetDropdownValue(_value, Mathf.RoundToInt(value)); break;
            }
        }

        #endregion
    }
}
