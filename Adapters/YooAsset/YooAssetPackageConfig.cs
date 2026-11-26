using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace EasyFramework
{
    /// <summary>一个 YooAsset 包的构建名称、资源服务器与运行设置。</summary>
    [CreateAssetMenu(fileName = "YooAssetPackage", menuName = "EasyFramework/Assets/YooAsset Package")]
    public sealed class YooAssetPackageConfig : AssetPackageConfig, IYooAssetPackageConfig, IHotUpdatePackageConfig
    {
        private const int MAX_DOWNLOAD_CONCURRENCY = 32;
        private const int MAX_DOWNLOAD_RETRIES = 5;

        [SerializeField] private string _packageName = "DefaultPackage";
        [SerializeField] private string _cdnRoot;
        [SerializeField] private string _fallbackCdnRoot;
        [Tooltip("仅供独立资源包的旧加载方式使用。受启动链管理的包使用发行配置中的清单版本。")]
        [SerializeField] private string _fallbackVersion;
        [SerializeField, Min(1)] private int _downloadConcurrency = 3;
        [SerializeField] private YooAssetWebFileSystem _miniGameFileSystem;

        [Header("受热更管理的包")]
        [SerializeField, Min(1)] private int _manifestTimeoutSeconds = 60;
        [SerializeField, Min(1)] private int _downloadTimeoutSeconds = 300;
        [SerializeField, Range(0, MAX_DOWNLOAD_RETRIES)] private int _downloadRetries = 2;

        /// <summary>YooAsset 收集器和发行产物使用的包名。</summary>
        public string PackageName => _packageName?.Trim();

        #region 公共方法

        /// <summary>创建按需请求当前清单的独立加载器。</summary>
        /// <returns>持有独立包生命周期的资源加载器。</returns>
        public override IAssetLoader CreateLoader()
        {
            if (string.IsNullOrWhiteSpace(_packageName))
                throw new InvalidOperationException("The YooAsset package name is required.");
            return new YooAssetLoader(PackageName, () => CreateOptions(Runtime.Mode), _fallbackVersion?.Trim());
        }

        /// <summary>在访问网络前检查启动链会使用的包配置。</summary>
        /// <param name="mode">当前宿主的运行模式。</param>
        /// <param name="version">发行锁定的 YooAsset 清单版本。</param>
        public void ValidateHotUpdateConfiguration(RuntimeMode mode, string version)
        {
            if (!YooAssetLoader.IsValidIdentifier(version))
                throw new InvalidOperationException($"YooAsset 包「{PackageName}」的清单版本「{version}」包含无效字符。");
            ValidateUpdateSettings(mode);
        }

        private void ValidateUpdateSettings(RuntimeMode mode)
        {
            if (!YooAssetLoader.IsValidIdentifier(PackageName))
                throw new InvalidOperationException("YooAsset 热更包名须使用字母、数字、点、下划线或连字符。");
            if (_downloadConcurrency < 1 || _downloadConcurrency > MAX_DOWNLOAD_CONCURRENCY)
                throw new InvalidOperationException($"YooAsset 包“{PackageName}”的下载并发须在 1 至 {MAX_DOWNLOAD_CONCURRENCY} 之间。");
            if (_manifestTimeoutSeconds < 1)
                throw new InvalidOperationException($"YooAsset 包“{PackageName}”的清单超时须大于零秒。");
            if (_downloadTimeoutSeconds < 1)
                throw new InvalidOperationException($"YooAsset 包“{PackageName}”的启动下载超时须大于零秒。");
            if (_downloadRetries < 0 || _downloadRetries > MAX_DOWNLOAD_RETRIES)
                throw new InvalidOperationException($"YooAsset 包“{PackageName}”的下载重试次数须在 0 至 {MAX_DOWNLOAD_RETRIES} 之间。");

#if (WEIXINMINIGAME || UNITY_WECHATMINIGAME || UNITY_MINIGAME) && !UNITY_EDITOR
            if (_miniGameFileSystem == null)
                throw new InvalidOperationException($"YooAsset 包“{PackageName}”未配置小游戏文件系统。");
            bool hasRequiredCdn = true;
#else
            bool hasRequiredCdn = mode == RuntimeMode.Online;
#endif
            bool hasFallbackCdn = !string.IsNullOrWhiteSpace(_fallbackCdnRoot);
            if (hasRequiredCdn || hasFallbackCdn || !string.IsNullOrWhiteSpace(_cdnRoot)) HttpsRoot(_cdnRoot);
            if (hasFallbackCdn) HttpsRoot(_fallbackCdnRoot);
        }

        /// <summary>创建由启动链锁定版本并显式准备的加载器。</summary>
        /// <returns>供启动链和资源模块共同使用的加载器。</returns>
        public IHotUpdateAssetLoader CreateHotUpdateLoader()
        {
            RuntimeMode mode = Runtime.Mode;
            ValidateUpdateSettings(mode);
            return new YooAssetLoader(PackageName, () => CreateOptions(mode), _downloadConcurrency,
                _manifestTimeoutSeconds, _downloadTimeoutSeconds, _downloadRetries);
        }

        #endregion

        #region 私有逻辑

        private InitializePackageOptions CreateOptions(RuntimeMode mode)
        {
            InitializePackageOptions options = CreatePlatformOptions(mode);
            options.BundleLoadingMaxConcurrency = Math.Max(1, _downloadConcurrency);
            options.AutoUnloadBundleWhenUnused = false;
            return options;
        }

        private InitializePackageOptions CreatePlatformOptions(RuntimeMode mode)
        {
#if (WEIXINMINIGAME || UNITY_WECHATMINIGAME || UNITY_MINIGAME) && !UNITY_EDITOR
            if (_miniGameFileSystem == null)
                throw new InvalidOperationException("Configure the WeChat YooAsset file system for this package.");
            return new WebPlayModeOptions
            {
                WebNetworkFileSystemParameters = _miniGameFileSystem.CreateParameters(CreateRemoteService(), Math.Max(1, _downloadConcurrency)),
            };
#else
            if (Application.isEditor && mode == RuntimeMode.Editor)
            {
                PackageBuildResult build = EditorSimulateBuildInvoker.Build(_packageName.Trim(), (int)EBundleType.VirtualAssetBundle);
                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory),
                };
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer || mode == RuntimeMode.Web)
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
            if (mode != RuntimeMode.Online)
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
                root.Scheme != Uri.UriSchemeHttps || root.Host.Length == 0 || root.UserInfo.Length != 0 ||
                root.Query.Length != 0 || root.Fragment.Length != 0)
                throw new InvalidOperationException("YooAsset CDN 根地址须为有效的 HTTPS 地址，不带凭据、查询参数或片段。");
            return root.AbsoluteUri.TrimEnd('/');
        }

        /// <summary>为同一包文件提供主备 CDN 地址。</summary>
        private sealed class RemoteService : IRemoteService
        {
            private readonly string _primary;
            private readonly string _fallback;

            internal RemoteService(string primary, string fallback)
            {
                _primary = primary;
                _fallback = fallback;
            }

            /// <summary>返回按优先级排列的同一版本文件地址。</summary>
            /// <param name="fileName">YooAsset 清单中的文件名。</param>
            /// <returns>主地址以及可选的备用地址。</returns>
            public IReadOnlyList<string> GetRemoteUrls(string fileName) =>
                _primary == _fallback
                    ? new[] { $"{_primary}/{fileName}" }
                    : new[] { $"{_primary}/{fileName}", $"{_fallback}/{fileName}" };
        }

        #endregion
    }
}
