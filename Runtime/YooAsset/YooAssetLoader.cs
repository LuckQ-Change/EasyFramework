#if EASY_YOOASSET
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace EasyFramework
{
    public class YooAssetLoader : IAssetLoader
    {
        private readonly string _packageName;
        private readonly EPlayMode _playMode;
        private readonly string _hostUrl;
        private ResourcePackage _package;

        private readonly Dictionary<string, AssetHandle> _handles = new Dictionary<string, AssetHandle>();

        public YooAssetLoader(string packageName = "DefaultPackage",
                              EPlayMode playMode = EPlayMode.EditorSimulateMode,
                              string hostUrl = null)
        {
            _packageName = packageName;
            _playMode = playMode;
            _hostUrl = hostUrl;
        }

        public async Task<bool> InitializeAsync()
        {
            if (!YooAssets.Initialized) YooAssets.Initialize();

            _package = YooAssets.TryGetPackage(_packageName) ?? YooAssets.CreatePackage(_packageName);

            InitializeParameters parameters;
            switch (_playMode)
            {
                case EPlayMode.EditorSimulateMode:
                    parameters = new EditorSimulateModeParameters
                    {
                        SimulateManifestFilePath = EditorSimulateModeHelper.SimulateBuild(_packageName),
                    };
                    break;
                case EPlayMode.OfflinePlayMode:
                    parameters = new OfflinePlayModeParameters();
                    break;
                case EPlayMode.HostPlayMode:
                    parameters = new HostPlayModeParameters
                    {
                        BuildinQueryServices = new GameQueryServices(),
                        RemoteServices = new RemoteServices(_hostUrl, _hostUrl),
                    };
                    break;
                default:
                    parameters = new OfflinePlayModeParameters();
                    break;
            }

            var op = _package.InitializeAsync(parameters);
            await op.Task;
            if (op.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[YooAsset] init failed: {op.Error}");
                return false;
            }
            YooAssets.SetDefaultPackage(_package);
            return true;
        }

        public async Task<T> LoadAsync<T>(string location) where T : class
        {
            if (_handles.TryGetValue(location, out var cached))
            {
                return cached.AssetObject as T;
            }

            var handle = _package.LoadAssetAsync<Object>(location);
            await handle.Task;
            if (handle.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[YooAsset] load failed: {location}, {handle.LastError}");
                handle.Release();
                return null;
            }
            _handles[location] = handle;
            return handle.AssetObject as T;
        }

        public void Release(string location)
        {
            if (_handles.TryGetValue(location, out var handle))
            {
                handle.Release();
                _handles.Remove(location);
            }
        }

        public void ReleaseAll()
        {
            foreach (var kv in _handles) kv.Value.Release();
            _handles.Clear();
        }

        private class RemoteServices : IRemoteServices
        {
            private readonly string _main;
            private readonly string _fallback;
            public RemoteServices(string main, string fallback) { _main = main; _fallback = fallback; }
            public string GetRemoteMainURL(string fileName) => $"{_main}/{fileName}";
            public string GetRemoteFallbackURL(string fileName) => $"{_fallback}/{fileName}";
        }

        private class GameQueryServices : IBuildinQueryServices
        {
            public bool Query(string packageName, string fileName, string fileCRC) => false;
        }
    }
}
#endif
