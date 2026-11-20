using System;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    /// <summary>单个资源包的异步后端。所有方法都由 Unity 主线程调用。</summary>
    public interface IAssetLoader
    {
        Task InitializeAsync();
        Task<LoadedAsset> LoadAsync(string address);
        Task TrimUnusedAsync();
        Task ShutdownAsync();
    }

    /// <summary>一次底层加载的所有权。释放回调只会执行一次。</summary>
    public sealed class LoadedAsset : IDisposable
    {
        private Action _release;
        public Object Asset { get; private set; }
        public long? EstimatedBytes { get; }

        public LoadedAsset(Object asset, Action release = null, long? estimatedBytes = null)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (estimatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            Asset = asset;
            _release = release;
            EstimatedBytes = estimatedBytes;
        }

        public void Dispose()
        {
            Action release = _release;
            _release = null;
            Asset = null;
            release?.Invoke();
        }
    }
}
