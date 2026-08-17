#if EASY_YOOASSET
using System.Collections.Generic;
using System.IO;
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
            if (!YooAssets.IsInitialized) YooAssets.Initialize();

            if (!YooAssets.TryGetPackage(_packageName, out _package))
                _package = YooAssets.CreatePackage(_packageName);
            if (_package.InitializeStatus == EOperationStatus.Succeeded)
                return await ActivateManifestAsync();

            InitializePackageOperation operation;
            switch (_playMode)
            {
                case EPlayMode.EditorSimulateMode:
                    PackageBuildResult buildResult = EditorSimulateBuildInvoker.Build(
                        _packageName,
                        (int)EBundleType.VirtualAssetBundle);
                    var simulateOptions = new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory),
                    };
                    operation = _package.InitializePackageAsync(simulateOptions);
                    break;

                case EPlayMode.OfflinePlayMode:
                    var offlineOptions = new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                    };
                    operation = _package.InitializePackageAsync(offlineOptions);
                    break;

                case EPlayMode.HostPlayMode:
                    if (string.IsNullOrWhiteSpace(_hostUrl))
                    {
                        Log.Error("[YooAsset] HostPlayMode 需要配置远端资源地址。");
                        return false;
                    }

                    var remoteService = new RemoteService(_hostUrl, _hostUrl);
                    var hostOptions = new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters =
                            FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                        CacheFileSystemParameters =
                            FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService),
                    };
                    operation = _package.InitializePackageAsync(hostOptions);
                    break;

                case EPlayMode.WebPlayMode:
                    var webOptions = new WebPlayModeOptions();
                    if (string.IsNullOrWhiteSpace(_hostUrl))
                    {
                        webOptions.WebServerFileSystemParameters =
                            FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                    }
                    else
                    {
                        webOptions.WebNetworkFileSystemParameters =
                            FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(
                                new RemoteService(_hostUrl, _hostUrl));
                    }
                    operation = _package.InitializePackageAsync(webOptions);
                    break;

                default:
                    Log.Error($"[YooAsset] 不支持的运行模式：{_playMode}");
                    return false;
            }

            await operation;

            if (operation.Status != EOperationStatus.Succeeded)
            {
                Log.Error($"[YooAsset] 初始化失败：{operation.Error}");
                return false;
            }

            return await ActivateManifestAsync();
        }

        private async Task<bool> ActivateManifestAsync()
        {
            RequestPackageVersionOperation versionOperation =
                _package.RequestPackageVersionAsync();
            await versionOperation;
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                Log.Error($"[YooAsset] 获取资源版本失败：{versionOperation.Error}");
                return false;
            }

            var manifestOptions = new LoadPackageManifestOptions(
                versionOperation.PackageVersion,
                60);
            LoadPackageManifestOperation manifestOperation =
                _package.LoadPackageManifestAsync(manifestOptions);
            await manifestOperation;
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                Log.Error($"[YooAsset] 加载资源清单失败：{manifestOperation.Error}");
                return false;
            }

            return true;
        }

        public async Task<T> LoadAsync<T>(string location) where T : class
        {
            if (_handles.TryGetValue(location, out var cached))
            {
                return cached.AssetObject as T;
            }

            string resolvedLocation = ResolveLocation(location);
            if (resolvedLocation == null)
            {
                Log.Error($"[YooAsset] 资源地址无效：{location}");
                return null;
            }

            var handle = _package.LoadAssetAsync<Object>(resolvedLocation);
            await handle;
            if (handle.Status != EOperationStatus.Succeeded)
            {
                Log.Error($"[YooAsset] 加载失败：{resolvedLocation}，{handle.Error}");
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

        private string ResolveLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || _package == null)
                return null;

            string trimmedLocation = location.Trim();
            if (_package.GetAssetInfo(trimmedLocation).IsValid)
                return trimmedLocation;

            // AddressByFileName 等规则会把资源路径转换为不带扩展名的文件名。
            // 仅当候选地址确实存在于当前清单时才采用，避免猜测并破坏自定义地址规则。
            string fileNameLocation = Path.GetFileNameWithoutExtension(
                trimmedLocation.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileNameLocation) ||
                string.Equals(fileNameLocation, trimmedLocation, System.StringComparison.Ordinal) ||
                !_package.GetAssetInfo(fileNameLocation).IsValid)
                return null;

            Log.Warn(
                $"[YooAsset] 资源地址“{trimmedLocation}”无效，" +
                $"已按当前资源清单解析为“{fileNameLocation}”。请将配置改为 YooAsset Address。");
            return fileNameLocation;
        }

        private sealed class RemoteService : IRemoteService
        {
            private readonly string _main;
            private readonly string _fallback;

            public RemoteService(string main, string fallback)
            {
                _main = main;
                _fallback = fallback;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                string mainUrl = CombineUrl(_main, fileName);
                string fallbackUrl = CombineUrl(_fallback, fileName);
                return string.Equals(mainUrl, fallbackUrl, System.StringComparison.Ordinal)
                    ? new[] { mainUrl }
                    : new[] { mainUrl, fallbackUrl };
            }

            private static string CombineUrl(string root, string fileName) =>
                $"{root.TrimEnd('/')}/{fileName}";
        }
    }
}
#endif
