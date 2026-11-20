using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public abstract class UIBindingRoot : MonoBehaviour
    {
        [SerializeField] private string _viewTypeName;
        [SerializeField] private string _viewScriptGuid;
        [SerializeField] private UIControlReference[] _controls = Array.Empty<UIControlReference>();

        public abstract UIPageKind Kind { get; }
        public virtual string PrefabLocation => string.Empty;
        public string ViewTypeName => _viewTypeName;
        public string ViewScriptGuid => _viewScriptGuid;
        public IReadOnlyList<UIControlReference> Controls => _controls;
        internal virtual UIObject RuntimeView => null;

        internal bool IsPage => IsPageKind(Kind);
        internal static bool IsPageKind(UIPageKind kind) => kind == UIPageKind.Screen || kind == UIPageKind.Popup;

        internal UIBindingRoot ParentBindingRoot => transform.parent == null
            ? null : transform.parent.GetComponentInParent<UIBindingRoot>(true);

        public void SetViewScript(string typeName, string scriptGuid)
        {
            _viewTypeName = typeName;
            _viewScriptGuid = scriptGuid;
        }

        public void SetControls(UIControlReference[] controls) => _controls = controls ?? Array.Empty<UIControlReference>();
    }
}
