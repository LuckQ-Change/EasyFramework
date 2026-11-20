using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YooAsset;
using Object = UnityEngine.Object;

namespace EasyFramework
{
    internal sealed class YooAssetLoader : IAssetLoader
    {
        private readonly string _packageName;
        private readonly Func<InitializePackageOptions> _createOptions;
        private readonly string _fallbackVersion;
        private ResourcePackage _package;

        public YooAssetLoader(string packageName, Func<InitializePackageOptions> createOptions, string fallbackVersion)
        {
            _packageName = packageName;
            _createOptions = createOptions;
            _fallbackVersion = fallbackVersion;
        }

        public async Task InitializeAsync()
        {
            if (!YooAssets.IsInitialized) YooAssets.Initialize();
            if (_package == null) _package = YooAssets.CreatePackage(_packageName);
            if (_package.InitializeStatus != EOperationStatus.Succeeded)
                await Complete(_package.InitializePackageAsync(_createOptions()));

            string version = null;
            try
            {
                var request = _package.RequestPackageVersionAsync();
                await Complete(request);
                version = request.PackageVersion;
                await LoadManifestAsync(version);
            }
            catch (InvalidOperationException) when (!string.IsNullOrEmpty(_fallbackVersion) && version != _fallbackVersion)
            {
                await LoadManifestAsync(_fallbackVersion);
            }
        }

        public async Task<LoadedAsset> LoadAsync(string address)
        {
            if (!_package.IsLocationValid(address))
                throw new KeyNotFoundException($"YooAsset package '{_packageName}' has no address '{address}'.");

            AssetHandle handle = _package.LoadAssetAsync<Object>(address);
            bool transferred = false;
            try
            {
                await handle;
                if (handle.Status != EOperationStatus.Succeeded || handle.AssetObject == null)
                    throw new InvalidOperationException($"Could not load '{_packageName}:{address}': {handle.Error}");
                var result = new LoadedAsset(handle.AssetObject, handle.Release);
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred) handle.Release();
            }
        }

        public Task TrimUnusedAsync() => Complete(_package.UnloadUnusedAssetsAsync());

        public async Task ShutdownAsync()
        {
            if (_package == null) return;
            if (_package.InitializeStatus != EOperationStatus.None)
                await Complete(_package.DestroyPackageAsync());
            YooAssets.RemovePackage(_packageName);
            _package = null;
        }

        private Task LoadManifestAsync(string version) =>
            Complete(_package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, 60)));

        private static async Task Complete(AsyncOperationBase operation)
        {
            await operation;
            if (operation.Status != EOperationStatus.Succeeded)
                throw new InvalidOperationException(operation.Error);
        }
    }
}
