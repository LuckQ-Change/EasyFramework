using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using YooAsset;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    /// <summary>持有单个 YooAsset 包，支持独立加载和由启动链锁定的版本。</summary>
    internal sealed class YooAssetLoader : IHotUpdateAssetLoader, IHotUpdateAssetCacheRepair
    {
        private const int STANDALONE_MANIFEST_TIMEOUT_SECONDS = 60;

        private readonly string _packageName;
        private readonly Func<InitializePackageOptions> _createOptions;
        private readonly string _fallbackVersion;
        private readonly bool _isUpdateManaged;
        private readonly int _manifestTimeoutSeconds;
        private readonly int _downloadTimeoutSeconds;
        private readonly int _downloadConcurrency;
        private readonly int _downloadRetries;

        private ResourcePackage _package;
        private string _lockedVersion;
        private bool _hasPrepared;
        private bool _isPreparing;
        private bool _canRepairCache;
        private bool _shouldOpenRequiredBundles;

        internal YooAssetLoader(string packageName, Func<InitializePackageOptions> createOptions, string fallbackVersion)
        {
            _packageName = packageName;
            _createOptions = createOptions;
            _fallbackVersion = fallbackVersion;
            _manifestTimeoutSeconds = STANDALONE_MANIFEST_TIMEOUT_SECONDS;
        }

        internal YooAssetLoader(string packageName, Func<InitializePackageOptions> createOptions,
            int downloadConcurrency, int manifestTimeoutSeconds, int downloadTimeoutSeconds, int downloadRetries)
        {
            _packageName = packageName;
            _createOptions = createOptions;
            _isUpdateManaged = true;
            _manifestTimeoutSeconds = manifestTimeoutSeconds;
            _downloadTimeoutSeconds = downloadTimeoutSeconds;
            _downloadConcurrency = downloadConcurrency;
            _downloadRetries = downloadRetries;
        }

        #region 公共方法

        /// <summary>准备锁定版本的清单和启动必需资源。</summary>
        /// <param name="version">本次发行记录指定的包清单版本。</param>
        /// <param name="requiredAddresses">本包内的资源地址，不含包别名。</param>
        /// <param name="token">取消本次准备的令牌。</param>
        /// <returns>指定清单及其必需 Bundle 准备完成的任务。</returns>
        public async Task PrepareVersionAsync(string version, IReadOnlyList<string> requiredAddresses, CancellationToken token)
        {
            if (!_isUpdateManaged) throw new InvalidOperationException($"YooAsset 包“{_packageName}”未交给热更启动链管理。");
            if (!IsValidIdentifier(version)) throw new ArgumentException("YooAsset 清单版本包含无效字符。", nameof(version));
            if (requiredAddresses == null) throw new ArgumentNullException(nameof(requiredAddresses));
            if (_isPreparing) throw new InvalidOperationException($"YooAsset 包“{_packageName}”正在准备版本。");
            if (_lockedVersion != null && !string.Equals(_lockedVersion, version, StringComparison.Ordinal))
                throw new InvalidOperationException($"YooAsset 包“{_packageName}”已锁定“{_lockedVersion}”，不能在当前会话切换到“{version}”。");

            token.ThrowIfCancellationRequested();
            _lockedVersion = version;
            _isPreparing = true;
            _hasPrepared = false;
            try
            {
                await InitializePackageAsync(version, token);
                if (!_package.PackageValid || !string.Equals(_package.GetPackageVersion(), version, StringComparison.Ordinal))
                {
                    var prefetch = _package.PrefetchManifestAsync(new PrefetchManifestOptions(version, _manifestTimeoutSeconds));
                    await CompleteAsync(prefetch, "预取指定清单", version, token);

                    // YooAsset 只通过当前清单查询地址。独占的包先启用指定清单，资源入口继续由启动链封闭。
                    await LoadManifestAsync(version, token);
                    VerifyActiveVersion();
                }

                var assets = ResolveRequiredAssets(requiredAddresses, version);
                await DownloadRequiredAsync(assets, version, token);
                if (_shouldOpenRequiredBundles) await OpenRequiredBundlesAsync(assets, requiredAddresses, version, token);
                token.ThrowIfCancellationRequested();
                VerifyActiveVersion();
                _hasPrepared = true;
            }
            finally { _isPreparing = false; }
        }

        /// <summary>由资源模块打开已准备的热更包，或按旧流程初始化独立包。</summary>
        /// <returns>资源包可访问时完成的任务。</returns>
        public async Task InitializeAsync()
        {
            if (_isUpdateManaged)
            {
                VerifyPreparedVersion();
                return;
            }

            await InitializePackageAsync(null, CancellationToken.None);
            string version = null;
            try
            {
                var request = _package.RequestPackageVersionAsync();
                await CompleteAsync(request, "请求最新版本", null, CancellationToken.None);
                version = request.PackageVersion;
                await LoadManifestAsync(version, CancellationToken.None);
            }
            catch (InvalidOperationException) when (!string.IsNullOrEmpty(_fallbackVersion) &&
                                                   !string.Equals(version, _fallbackVersion, StringComparison.Ordinal))
            {
                await LoadManifestAsync(_fallbackVersion, CancellationToken.None);
            }
        }

        /// <summary>从当前清单加载资源并转移底层句柄所有权。</summary>
        /// <param name="address">资源包内地址。</param>
        /// <returns>持有资源和归还回调的加载结果。</returns>
        public async Task<LoadedAsset> LoadAsync(string address)
        {
            if (_isUpdateManaged) VerifyPreparedVersion();
            if (!_package.IsLocationValid(address))
                throw new KeyNotFoundException($"YooAsset 包“{_packageName}”版本“{_package.GetPackageVersion()}”没有地址“{address}”。");

            AssetHandle handle = _package.LoadAssetAsync<Object>(address);
            bool hasTransferred = false;
            try
            {
                await handle;
                if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
                    throw Failure($"加载资源“{address}”", _package.GetPackageVersion(), handle.Error);
                var result = new LoadedAsset(handle.AssetObject, handle.Release);
                hasTransferred = true;
                return result;
            }
            finally
            {
                if (!hasTransferred) handle.Release();
            }
        }

        /// <summary>回收当前包中未持有的资源。</summary>
        /// <returns>底层回收完成的任务。</returns>
        public Task TrimUnusedAsync() => _package == null || !_package.PackageValid
            ? Task.CompletedTask
            : CompleteAsync(_package.UnloadUnusedAssetsAsync(), "回收空闲资源", _package.GetPackageVersion(), CancellationToken.None);

        /// <summary>从可修复的沙盒缓存逐出指定地址的主 Bundle，供相同版本重新下载。</summary>
        /// <param name="address">已释放读取句柄且发生完整性错误的包内地址。</param>
        /// <param name="token">取消修复的令牌。</param>
        /// <returns>指定缓存文件处理完成的任务。</returns>
        public async Task InvalidateCachedAssetAsync(string address, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            VerifyPreparedVersion();
            if (!_canRepairCache)
                throw new NotSupportedException($"YooAsset 包“{_packageName}”未使用可按地址修复的在线沙盒缓存。");
            if (string.IsNullOrWhiteSpace(address) || !_package.IsLocationValid(address))
                throw Failure("定位损坏的资源", _lockedVersion, $"清单中不存在“{address}”。");

            _hasPrepared = false;
            // 先撤销已归还的加载器对旧 Bundle 的缓存，再只逐出该地址所属的 Bundle 文件。
            await CompleteAsync(_package.UnloadUnusedAssetsAsync(), "释放待修复的 Bundle", _lockedVersion, token);
            var options = new ClearCacheOptions(ClearCacheMethods.ClearBundleFilesByLocations, address);
            await CompleteAsync(_package.ClearCacheAsync(options), $"清理地址“{address}”的缓存", _lockedVersion, token);
        }

        /// <summary>回收当前加载器创建的独占包。</summary>
        /// <returns>包和相关操作完全退出时完成的任务。</returns>
        public async Task ShutdownAsync()
        {
            ResourcePackage package = _package;
            if (package == null) return;
            if (ContainsOwnedPackage(package))
            {
                if (package.InitializeStatus != EOperationStatus.None)
                    await CompleteAsync(package.DestroyPackageAsync(), "结束资源包", _lockedVersion, CancellationToken.None);
                if (ContainsOwnedPackage(package)) YooAssets.RemovePackage(_packageName);
            }
            _package = null;
            _hasPrepared = false;
        }

        #endregion

        #region 私有逻辑

        internal static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value[value.Length - 1] == '.') return false;
            foreach (char character in value)
                if (!char.IsLetterOrDigit(character) && character != '.' && character != '_' && character != '-') return false;
            return true;
        }

        private async Task InitializePackageAsync(string version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (_package == null)
            {
                if (YooAssets.TryGetPackage(_packageName, out _))
                    throw Failure("创建资源包", version, "同名包已由另一个加载器或宿主持有。");
                _package = YooAssets.CreatePackage(_packageName);
            }
            if (_package.InitializeStatus == EOperationStatus.Succeeded) return;

            InitializePackageOptions options = _createOptions();
            _canRepairCache = options is HostPlayModeOptions;
            _shouldOpenRequiredBundles = options is WebPlayModeOptions || options is OfflinePlayModeOptions;
            await CompleteAsync(_package.InitializePackageAsync(options), "初始化文件系统", version, token);
        }

        private bool ContainsOwnedPackage(ResourcePackage package) => YooAssets.IsInitialized &&
            YooAssets.TryGetPackage(_packageName, out ResourcePackage current) && ReferenceEquals(package, current);

        private Task LoadManifestAsync(string version, CancellationToken token) =>
            CompleteAsync(_package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, _manifestTimeoutSeconds)),
                "启用清单", version, token);

        private AssetInfo[] ResolveRequiredAssets(IReadOnlyList<string> requiredAddresses, string version)
        {
            var assets = new AssetInfo[requiredAddresses.Count];
            for (int index = 0; index < requiredAddresses.Count; index++)
            {
                string address = requiredAddresses[index];
                if (string.IsNullOrWhiteSpace(address) || !_package.IsLocationValid(address))
                    throw Failure("检查启动资源地址", version, $"清单中不存在“{address}”。");
                assets[index] = _package.GetAssetInfo(address);
            }
            return assets;
        }

        private async Task DownloadRequiredAsync(AssetInfo[] assets, string version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (assets.Length == 0) return;

            var downloader = _package.CreateResourceDownloader(new BundleDownloaderOptions(assets, false,
                _downloadConcurrency, _downloadRetries));
            if (downloader.TotalDownloadCount == 0) return;

            string fileError = null;
            downloader.DownloadError += CaptureError;
            try
            {
                var timeout = TimeSpan.FromSeconds(_downloadTimeoutSeconds);
                var elapsed = Stopwatch.StartNew();
                downloader.StartDownload();
                while (!downloader.IsDone && !token.IsCancellationRequested && elapsed.Elapsed < timeout) await Task.Yield();
                bool hasTimedOut = !downloader.IsDone && !token.IsCancellationRequested;
                if (!downloader.IsDone) downloader.CancelDownload();
                await downloader;
                token.ThrowIfCancellationRequested();
                if (hasTimedOut)
                    throw Failure("下载启动资源及依赖", version, $"超过 {_downloadTimeoutSeconds} 秒，已完成 {downloader.CurrentDownloadCount}/{downloader.TotalDownloadCount} 个文件。");
                if (downloader.Status != EOperationStatus.Succeeded)
                    throw Failure("下载启动资源及依赖", version, fileError ?? downloader.Error);
            }
            finally { downloader.DownloadError -= CaptureError; }

            void CaptureError(DownloadErrorEventArgs args) => fileError = $"“{args.FileName}” {args.ErrorInfo}";
        }

        private async Task OpenRequiredBundlesAsync(AssetInfo[] assets, IReadOnlyList<string> addresses, string version, CancellationToken token)
        {
            // 只读内置文件系统和部分 Web 文件系统不提供主动下载。打开一次底层 Bundle 以确认实际可用。
            for (int index = 0; index < assets.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                BundleFileHandle handle = _package.LoadBundleFileAsync(assets[index]);
                try
                {
                    await handle;
                    token.ThrowIfCancellationRequested();
                    if (handle.Status != EOperationStatus.Succeeded)
                        throw Failure($"打开启动资源“{addresses[index]}”的 Bundle", version, handle.Error);
                }
                finally { handle.Release(); }
            }
        }

        private void VerifyPreparedVersion()
        {
            if (!_hasPrepared) throw Failure("打开资源包", _lockedVersion, "该清单及启动资源尚未准备完成。");
            VerifyActiveVersion();
        }

        private void VerifyActiveVersion()
        {
            if (_package == null || !_package.PackageValid || !string.Equals(_package.GetPackageVersion(), _lockedVersion, StringComparison.Ordinal))
                throw Failure("确认指定清单", _lockedVersion, "当前清单与锁定版本不一致。");
        }

        private async Task CompleteAsync(AsyncOperationBase operation, string step, string version, CancellationToken token)
        {
            // 清单类操作由 YooAsset 自己收尾。等待完成后再取消，避免与下一次准备或关闭竞争。
            await operation;
            token.ThrowIfCancellationRequested();
            if (operation.Status != EOperationStatus.Succeeded)
                throw Failure(step, version, operation.Error);
        }

        private InvalidOperationException Failure(string step, string version, string error) =>
            new InvalidOperationException($"YooAsset 包“{_packageName}”版本“{version ?? "未选定"}”{step}失败。{error}");

        #endregion
    }
}
