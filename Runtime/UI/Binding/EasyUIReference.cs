using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    [Serializable]
    public sealed class EasyUIReferenceEntry
    {
        [SerializeField] private string _propertyName;
        [SerializeField] private Component _target;
        [SerializeField] private string _resourceLocation;

        public string PropertyName => _propertyName;
        public Component Target => _target;
        public string ResourceLocation => _resourceLocation;
    }

    /// <summary>FGUI-style marker used to generate strongly typed properties and resource locations.</summary>
    [AddComponentMenu("EasyFramework/UI/Reference Marker")]
    [DisallowMultipleComponent]
    public sealed class EasyUIReference : MonoBehaviour
    {
        [SerializeField] private List<EasyUIReferenceEntry> _entries = new List<EasyUIReferenceEntry>();
        public IReadOnlyList<EasyUIReferenceEntry> Entries => _entries;
    }
}
