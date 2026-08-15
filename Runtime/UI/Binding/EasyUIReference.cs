using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    [Serializable]
    public sealed class EasyUIReferenceEntry
    {
        [SerializeField] private string _key;
        [SerializeField] private Component _target;
        [SerializeField] private string _resourceLocation;

        public string Key => _key;
        public Component Target => _target;
        public string ResourceLocation => _resourceLocation;
    }

    /// <summary>Serialized component references exposed through EasyUIBinding.Get.</summary>
    [AddComponentMenu("EasyFramework/UI/Reference Marker")]
    [DisallowMultipleComponent]
    public sealed class EasyUIReference : MonoBehaviour
    {
        [SerializeField] private List<EasyUIReferenceEntry> _entries = new List<EasyUIReferenceEntry>();
        public IReadOnlyList<EasyUIReferenceEntry> Entries => _entries;
    }
}
