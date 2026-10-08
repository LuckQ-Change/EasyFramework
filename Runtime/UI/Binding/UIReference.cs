using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    [Serializable]
    public sealed class UIReferenceEntry
    {
        [SerializeField] private string _key;
        [SerializeField] private Component _target;
        [SerializeField] private string _resourceLocation;

        public string Key => _key;
        public Component Target => _target;
        public string ResourceLocation => _resourceLocation;
    }

    /// <summary>通过 UIBinding.Get 访问的序列化组件引用。</summary>
    [AddComponentMenu("EasyFramework/UI/Reference Marker")]
    [DisallowMultipleComponent]
    public sealed class UIReference : MonoBehaviour
    {
        [SerializeField] private List<UIReferenceEntry> _entries = new List<UIReferenceEntry>();
        public IReadOnlyList<UIReferenceEntry> Entries => _entries;
    }
}
