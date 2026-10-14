using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>隔离资源系统实现；加载和释放必须按相同地址配对。</summary>
    public interface IAssetLoader
    {
        Task<bool> InitializeAsync();
        Task<T> LoadAsync<T>(string location) where T : class;
        void Release(string location);
        void ReleaseAll();
    }
}
