using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    /// <summary>使用项目提供的现成对象，供演示和测试。对象由创建者管理。</summary>
    public sealed class MemoryAssetLoader : IAssetLoader
    {
        private readonly Dictionary<string, Object> _assets = new Dictionary<string, Object>(StringComparer.Ordinal);

        public void Add(string address, Object asset)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Asset address is required.", nameof(address));
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            _assets.Add(address.Trim(), asset);
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<LoadedAsset> LoadAsync(string address) =>
            _assets.TryGetValue(address, out Object asset) && asset != null
                ? Task.FromResult(new LoadedAsset(asset))
                : Task.FromException<LoadedAsset>(new KeyNotFoundException($"Asset '{address}' was not found."));

        public Task TrimUnusedAsync() => Task.CompletedTask;
        public Task ShutdownAsync()
        {
            _assets.Clear();
            return Task.CompletedTask;
        }
    }
}
