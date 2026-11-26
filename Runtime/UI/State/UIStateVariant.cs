using System;
using UnityEngine;

namespace EasyFramework.UI
{
    [Flags]
    /// <summary>标识状态系统可覆盖的节点属性。</summary>
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
    /// <summary>保存一组受控属性的基准或覆盖值。</summary>
    public sealed class UIStateVariant
    {
        [SerializeField] private UIStateProperty _properties;
        [SerializeField] private bool _active = true;
        [SerializeField] private bool _interactable = true;
        [SerializeField] private Color _color = Color.white;
        [SerializeField, Range(0f, 1f)] private float _alpha = 1f;
        [SerializeField] private Sprite _sprite;
        [SerializeField, TextArea] private string _text;
        [SerializeField] private float _value;
        [SerializeField] private int _spriteIndex;

        /// <summary>本组数据包含的属性掩码。</summary>
        public UIStateProperty Properties => _properties;
        /// <summary>节点激活值。</summary>
        public bool Active => _active;
        /// <summary>控件交互开关。</summary>
        public bool Interactable => _interactable;
        /// <summary>图形颜色。</summary>
        public Color Color => _color;
        /// <summary>透明度。</summary>
        public float Alpha => _alpha;
        /// <summary>图像资源。</summary>
        public Sprite Sprite => _sprite;
        /// <summary>控件文本。</summary>
        public string Text => _text;
        /// <summary>控件数值。</summary>
        public float Value => _value;
        /// <summary>图像槽位索引。</summary>
        public int SpriteIndex => _spriteIndex;

        /// <summary>设置本组数据包含的属性掩码。</summary>
        /// <param name="properties">已记录的字段。</param>
        public void SetProperties(UIStateProperty properties) => _properties = properties;
        /// <summary>设置记录的节点激活状态。</summary>
        /// <param name="value">节点是否激活。</param>
        public void SetActive(bool value) => _active = value;
        /// <summary>设置记录的控件交互开关。</summary>
        /// <param name="value">控件是否可交互。</param>
        public void SetInteractable(bool value) => _interactable = value;
        /// <summary>设置记录的图形颜色。</summary>
        /// <param name="value">图形颜色。</param>
        public void SetColor(Color value) => _color = value;
        /// <summary>设置记录的透明度。</summary>
        /// <param name="value">透明度。</param>
        public void SetAlpha(float value) => _alpha = value;
        /// <summary>设置记录的图像资源。</summary>
        /// <param name="value">图像资源。</param>
        public void SetSprite(Sprite value) => _sprite = value;
        /// <summary>设置记录的控件文本。</summary>
        /// <param name="value">控件文本。</param>
        public void SetText(string value) => _text = value;
        /// <summary>设置记录的控件数值。</summary>
        /// <param name="value">控件数值。</param>
        public void SetValue(float value) => _value = value;
        /// <summary>设置记录的图像槽位索引。</summary>
        /// <param name="value">图像槽位索引。</param>
        public void SetSpriteIndex(int value) => _spriteIndex = value;
    }
}
