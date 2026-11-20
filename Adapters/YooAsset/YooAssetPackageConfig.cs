using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace EasyFramework
{
    /// <summary>一个 YooAsset 包的构建名称、资源服务器与运行设置。</summary>
    [CreateAssetMenu(fileName = "YooAssetPackage", menuName = "EasyFramework/Assets/YooAsset Package")]
    public sealed class YooAssetPackageConfig : AssetPackageConfig
    {
        [SerializeField] private string _packageName = "DefaultPackage";
        [SerializeField] private string _cdnRoot;
        [SerializeField] private string _fallbackCdnRoot;
        [SerializeField] private string _fallbackVersion;
        [SerializeField, Min(1)] private int _downloadConcurrency = 3;
        [SerializeField] private YooAssetWebFileSystem _miniGameFileSystem;

        public string PackageName => _packageName?.Trim();

        public override IAssetLoader CreateLoader()
        {
            if (string.IsNullOrWhiteSpace(_packageName))
                throw new InvalidOperationException("The YooAsset package name is required.");
            return new YooAssetLoader(_packageName.Trim(), CreateOptions, _fallbackVersion?.Trim());
        }

        private InitializePackageOptions CreateOptions()
        {
            InitializePackageOptions options = CreatePlatformOptions();
            options.BundleLoadingMaxConcurrency = Math.Max(1, _downloadConcurrency);
            options.AutoUnloadBundleWhenUnused = false;
            return options;
        }

        private InitializePackageOptions CreatePlatformOptions()
        {
#if (WEIXINMINIGAME || UNITY_WECHATMINIGAME || UNITY_MINIGAME) && !UNITY_EDITOR
            if (_miniGameFileSystem == null)
                throw new InvalidOperationException("Configure the WeChat YooAsset file system for this package.");
            return new WebPlayModeOptions
            {
                WebNetworkFileSystemParameters = _miniGameFileSystem.CreateParameters(CreateRemoteService(), Math.Max(1, _downloadConcurrency)),
            };
#else
            if (Application.isEditor && Runtime.IsEditor)
            {
                PackageBuildResult build = EditorSimulateBuildInvoker.Build(_packageName.Trim(), (int)EBundleType.VirtualAssetBundle);
                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory),
                };
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer || Runtime.IsWeb)
            {
                if (string.IsNullOrWhiteSpace(_cdnRoot))
                {
                    return new WebPlayModeOptions
                    {
                        WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(),
                    };
                }
                FileSystemParameters web = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(CreateRemoteService());
                ApplyDownloadLimits(web);
                return new WebPlayModeOptions { WebNetworkFileSystemParameters = web };
            }

            FileSystemParameters builtin = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            if (!Runtime.IsOnline)
                return new OfflinePlayModeOptions { BuiltinFileSystemParameters = builtin };

            builtin.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
            FileSystemParameters sandbox = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(CreateRemoteService());
            ApplyDownloadLimits(sandbox);
            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = builtin,
                CacheFileSystemParameters = sandbox,
            };
#endif
        }

        private void ApplyDownloadLimits(FileSystemParameters fileSystem)
        {
            fileSystem.AddParameter(EFileSystemParameter.DownloadMaxConcurrency, Math.Max(1, _downloadConcurrency));
            fileSystem.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame, 1);
        }

        private IRemoteService CreateRemoteService()
        {
            string primary = HttpsRoot(_cdnRoot);
            string fallback = string.IsNullOrWhiteSpace(_fallbackCdnRoot) ? primary : HttpsRoot(_fallbackCdnRoot);
            return new RemoteService(primary, fallback);
        }

        private static string HttpsRoot(string url)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri root) ||
                root.Scheme != Uri.UriSchemeHttps || root.Query.Length != 0 || root.Fragment.Length != 0)
                throw new InvalidOperationException("Set a valid HTTPS CDN root for the YooAsset package.");
            return root.AbsoluteUri.TrimEnd('/');
        }

        private sealed class RemoteService : IRemoteService
        {
            private readonly string _primary;
            private readonly string _fallback;

            public RemoteService(string primary, string fallback)
            {
                _primary = primary;
                _fallback = fallback;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName) =>
                _primary == _fallback
                    ? new[] { $"{_primary}/{fileName}" }
                    : new[] { $"{_primary}/{fileName}", $"{_fallback}/{fileName}" };
        }
    }
}
