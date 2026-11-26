using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>保存单个受控节点的基准值和按状态标识关联的覆盖。</summary>
    [Serializable]
    public sealed class UIStateTargetBinding
    {
        /// <summary>将一组属性覆盖关联到控制器中的稳定状态标识。</summary>
        [Serializable]
        private sealed class StateOverride
        {
            [SerializeField] private string _stateId;
            [SerializeField] private UIStateVariant _value;

            internal StateOverride(string stateId, UIStateVariant value)
            {
                _stateId = stateId;
                _value = value;
            }

            internal string StateId => _stateId;
            internal UIStateVariant Value => _value;
        }

        [SerializeField] private GameObject _target;
        [SerializeField] private UIStateVariant _baseline = new UIStateVariant();
        [SerializeField] private List<StateOverride> _overrides = new List<StateOverride>();
        [NonSerialized] private UIStateTargetAccess _access;

        /// <summary>受控制的节点。</summary>
        public GameObject Target => _target;

        /// <summary>未覆盖的属性使用的基准值。通过控制器提交修改。</summary>
        public UIStateVariant Baseline => _baseline;

        /// <summary>当前由控制器管理的字段。</summary>
        public UIStateProperty Properties => _baseline?.Properties ?? UIStateProperty.None;

        internal UIStateTargetBinding(GameObject target, UIStateVariant baseline)
        {
            _target = target;
            _baseline = baseline;
        }

        internal void ApplyFields(string stateId)
        {
            if (_target == null) return;
            _access ??= new UIStateTargetAccess(_target);
            _access.ApplyFields(_baseline, FindOverride(stateId));
        }

        internal void ApplyInactive(string stateId)
        {
            if (_target == null || (Properties & UIStateProperty.Active) == 0) return;
            _access ??= new UIStateTargetAccess(_target);
            _access.ApplyInactive(_baseline, FindOverride(stateId));
        }

        internal bool ApplyActive(string stateId)
        {
            if (_target == null || (Properties & UIStateProperty.Active) == 0) return false;
            _access ??= new UIStateTargetAccess(_target);
            return _access.ApplyActive(_baseline, FindOverride(stateId));
        }

        internal void ReapplySprite(string stateId)
        {
            if (_target == null || (Properties & (UIStateProperty.Sprite | UIStateProperty.SpriteIndex)) == 0) return;
            _access ??= new UIStateTargetAccess(_target);
            _access.ReapplySprite(_baseline, FindOverride(stateId));
        }

        internal UIStateVariant FindOverride(string stateId)
        {
            foreach (StateOverride item in _overrides)
                if (item != null && item.StateId == stateId) return item.Value;
            return null;
        }

        internal bool SetOverride(string stateId, UIStateVariant value)
        {
            UIStateProperty fields = Properties & value.Properties;
            if (fields == UIStateProperty.None) return false;
            UIStateVariant current = FindOverride(stateId);
            if (current == null)
            {
                current = new UIStateVariant();
                _overrides.Add(new StateOverride(stateId, current));
            }
            return UIStateVariantValues.Merge(current, value, fields);
        }

        internal bool ClearOverride(string stateId, UIStateProperty fields)
        {
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                StateOverride item = _overrides[i];
                if (item == null || item.StateId != stateId || item.Value == null) continue;
                return ClearFields(i, fields);
            }
            return false;
        }

        internal bool RemoveFields(UIStateProperty fields)
        {
            if ((Properties & fields) == 0) return false;
            _baseline.SetProperties(Properties & ~fields);
            for (int i = _overrides.Count - 1; i >= 0; i--)
                ClearFields(i, fields);
            return true;
        }

        private bool ClearFields(int index, UIStateProperty fields)
        {
            UIStateVariant value = _overrides[index]?.Value;
            if (value == null || (value.Properties & fields) == 0) return false;
            UIStateProperty remaining = value.Properties & ~fields;
            if (remaining == UIStateProperty.None) _overrides.RemoveAt(index);
            else value.SetProperties(remaining);
            return true;
        }
    }

    /// <summary>复制或合并状态值，按掩码保留其他字段。</summary>
    internal static class UIStateVariantValues
    {
        internal static UIStateVariant Copy(UIStateVariant source, UIStateProperty fields)
        {
            var copy = new UIStateVariant();
            if (source != null) Merge(copy, source, source.Properties & fields);
            return copy;
        }

        internal static bool Merge(UIStateVariant target, UIStateVariant source, UIStateProperty fields)
        {
            bool hasChanged = (target.Properties & fields) != fields;
            if ((fields & UIStateProperty.Active) != 0 && target.Active != source.Active)
            {
                target.SetActive(source.Active);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Interactable) != 0 && target.Interactable != source.Interactable)
            {
                target.SetInteractable(source.Interactable);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Color) != 0 && !target.Color.Equals(source.Color))
            {
                target.SetColor(source.Color);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Alpha) != 0 && !target.Alpha.Equals(source.Alpha))
            {
                target.SetAlpha(source.Alpha);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Sprite) != 0 && target.Sprite != source.Sprite)
            {
                target.SetSprite(source.Sprite);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Text) != 0 && target.Text != source.Text)
            {
                target.SetText(source.Text);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.Value) != 0 && !target.Value.Equals(source.Value))
            {
                target.SetValue(source.Value);
                hasChanged = true;
            }
            if ((fields & UIStateProperty.SpriteIndex) != 0 && target.SpriteIndex != source.SpriteIndex)
            {
                target.SetSpriteIndex(source.SpriteIndex);
                hasChanged = true;
            }
            target.SetProperties(target.Properties | fields);
            return hasChanged;
        }
    }
}
