using System;
using UnityEngine;

namespace EasyFramework.UI
{
    [Flags]
    public enum UIStateProperty
    {
        None = 0,
        Active = 1 << 0,
        Interactable = 1 << 1,
        Color = 1 << 2,
        Alpha = 1 << 3,
        Sprite = 1 << 4,
        Text = 1 << 5,
        Value = 1 << 6,
        SpriteIndex = 1 << 7,
        All = ~0,
    }

    [Serializable]
    public sealed class UIStateVariant
    {
        [SerializeField] private string _state;
        [SerializeField] private UIStateProperty _properties;
        [SerializeField] private bool _active = true;
        [SerializeField] private bool _interactable = true;
        [SerializeField] private Color _color = Color.white;
        [SerializeField, Range(0f, 1f)] private float _alpha = 1f;
        [SerializeField] private Sprite _sprite;
        [SerializeField, TextArea] private string _text;
        [SerializeField] private float _value;
        [SerializeField] private int _spriteIndex;

        public string State => _state;
        public UIStateProperty Properties => _properties;
        public bool Active => _active;
        public bool Interactable => _interactable;
        public Color Color => _color;
        public float Alpha => _alpha;
        public Sprite Sprite => _sprite;
        public string Text => _text;
        public float Value => _value;
        public int SpriteIndex => _spriteIndex;

        public UIStateVariant(string state = null) => _state = state;

        public void SetState(string state) => _state = state;
        public void SetProperties(UIStateProperty properties) => _properties = properties;
        public void SetActive(bool value) => _active = value;
        public void SetInteractable(bool value) => _interactable = value;
        public void SetColor(Color value) => _color = value;
        public void SetAlpha(float value) => _alpha = value;
        public void SetSprite(Sprite value) => _sprite = value;
        public void SetText(string value) => _text = value;
        public void SetValue(float value) => _value = value;
        public void SetSpriteIndex(int value) => _spriteIndex = value;
    }
}
