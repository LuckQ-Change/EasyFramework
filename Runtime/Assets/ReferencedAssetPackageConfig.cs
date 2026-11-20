using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    /// <summary>随启动资产打入的显式引用包，适用于少量首屏资源与演示。</summary>
    [CreateAssetMenu(menuName = "EasyFramework/Assets/Referenced Package")]
    public sealed class ReferencedAssetPackageConfig : AssetPackageConfig
    {
        [Serializable]
        public sealed class Entry
        {
            public string Address;
            public Object Asset;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Entries => _entries;

        public override IAssetLoader CreateLoader()
        {
            var loader = new MemoryAssetLoader();
            foreach (Entry entry in _entries) loader.Add(entry.Address, entry.Asset);
            return loader;
        }
    }
}
