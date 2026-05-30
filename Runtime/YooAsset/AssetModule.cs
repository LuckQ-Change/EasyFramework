using System;
using System.Threading.Tasks;

namespace EasyFramework
{
    public class AssetModule : ModuleSingleton<AssetModule>
    {
        private IAssetLoader _loader;

        public bool IsReady { get; private set; }

        public void SetLoader(IAssetLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public async Task<bool> InitializeAsync()
        {
            if (_loader == null)
            {
                Log.Error("[Asset] no loader assigned, call SetLoader first.");
                return false;
            }
            IsReady = await _loader.InitializeAsync();
            return IsReady;
        }

        public Task<T> LoadAsync<T>(string location) where T : class
        {
            if (!IsReady)
            {
                Log.Error($"[Asset] not initialized, cannot load {location}");
                return Task.FromResult<T>(null);
            }
            return _loader.LoadAsync<T>(location);
        }

        public void Release(string location)
        {
            _loader?.Release(location);
        }

        public void ReleaseAll()
        {
            _loader?.ReleaseAll();
        }

        protected override void OnShutdown()
        {
            ReleaseAll();
            IsReady = false;
            _loader = null;
        }
    }
}
