using System;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [DisallowMultipleComponent]
    internal sealed class UIBackground : MonoBehaviour
    {
        private Image _image;
        private Button _button;
        private Action _clicked;

        internal void Configure(Color color, bool blocksInput, Action clicked)
        {
            EnsureComponents();
            _image.color = color;
            _clicked = clicked;
            _image.raycastTarget = blocksInput || clicked != null;
            _button.enabled = clicked != null;
            _button.interactable = clicked != null;
        }

        private void EnsureComponents()
        {
            if (_image == null) _image = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            if (_button == null)
            {
                _button = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
                _button.transition = Selectable.Transition.None;
                _button.targetGraphic = _image;
                _button.onClick.AddListener(HandleClick);
            }
        }

        private void HandleClick() => _clicked?.Invoke();
    }
}
