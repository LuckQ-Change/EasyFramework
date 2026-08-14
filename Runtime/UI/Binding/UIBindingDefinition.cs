using System;
using UnityEngine;

namespace EasyFramework.UI
{
    public enum UIBindingProperty
    {
        Text,
        Interactable,
        Value,
        IsOn,
        Color,
        Sprite,
        Alpha,
        FillAmount,
        SpriteIndex,
        Active,
    }

    [Serializable]
    public sealed class UIBindingDefinition
    {
        [SerializeField] private Component _target;
        [SerializeField] private UIBindingProperty _targetProperty;
        [SerializeField] private string _sourceKey;
        [SerializeField] private string _format = "{0}";
        [SerializeField] private bool _twoWay;

        public Component Target => _target;
        public UIBindingProperty TargetProperty => _targetProperty;
        public string SourceKey => _sourceKey;
        public string Format => _format;
        public bool TwoWay => _twoWay;

        public UIBindingDefinition(
            Component target,
            UIBindingProperty targetProperty,
            string sourceKey,
            string format = "{0}",
            bool twoWay = false)
        {
            _target = target;
            _targetProperty = targetProperty;
            _sourceKey = sourceKey;
            _format = string.IsNullOrEmpty(format) ? "{0}" : format;
            _twoWay = twoWay;
        }
    }
}
