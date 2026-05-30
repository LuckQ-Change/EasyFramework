using System.Threading.Tasks;

namespace EasyFramework
{
    public interface IAssetLoader
    {
        Task<bool> InitializeAsync();
        Task<T> LoadAsync<T>(string location) where T : class;
        void Release(string location);
        void ReleaseAll();
    }
}
