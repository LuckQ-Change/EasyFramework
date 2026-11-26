using System;
using System.Collections.Generic;
using UnityEditor;

namespace EasyFramework.Editor
{
    /// <summary>在一次构建或发行预检中缓存各资源包的实际收集地址。</summary>
    internal sealed class EditorPackageAddresses
    {
        private readonly Dictionary<AssetPackageConfig, IReadOnlyDictionary<string, string>> _collected = new();

        internal IReadOnlyDictionary<string, string> Collect(AssetPackageConfig package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (_collected.TryGetValue(package, out IReadOnlyDictionary<string, string> addresses)) return addresses;

            addresses = package is ReferencedAssetPackageConfig references
                ? CollectReferences(references)
                : EditorPackageAddressProviders.Collect(package);
            _collected.Add(package, addresses);
            return addresses;
        }

        private static IReadOnlyDictionary<string, string> CollectReferences(ReferencedAssetPackageConfig package)
        {
            IReadOnlyList<ReferencedAssetPackageConfig.Entry> entries = package.Entries;
            if (entries == null) throw new InvalidOperationException($"直接引用包 {package.Alias} 缺少条目列表。");

            var addresses = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                ReferencedAssetPackageConfig.Entry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Address) || entry.Asset == null)
                    throw new InvalidOperationException($"直接引用包 {package.Alias} 的第 {i + 1} 项缺少包内地址或资源。");

                string address = entry.Address.Trim();
                string path = AssetDatabase.GetAssetPath(entry.Asset);
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException($"直接引用包 {package.Alias} 的地址 {address} 没有指向工程资产。");
                if (!addresses.TryAdd(address, path))
                    throw new InvalidOperationException($"直接引用包 {package.Alias} 的地址 {address} 重复。");
            }
            return addresses;
        }
    }
}
