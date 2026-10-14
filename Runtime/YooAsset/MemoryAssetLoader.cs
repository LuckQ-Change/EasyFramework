using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>供测试和小型演示使用的内存资源表；资源由注册方提供。</summary>
    public class MemoryAssetLoader : IAssetLoader
    {
        private readonly Dictionary<string, object> _assets = new Dictionary<string, object>();

        public void Add(string location, object asset)
        {
            _assets[location] = asset;
        }

        public Task<bool> InitializeAsync() => Task.FromResult(true);

        public Task<T> LoadAsync<T>(string location) where T : class
        {
            _assets.TryGetValue(location, out var obj);
            return Task.FromResult(obj as T);
        }

        public void Release(string location)
        {
        }

        public void ReleaseAll()
        {
        }
    }
}
