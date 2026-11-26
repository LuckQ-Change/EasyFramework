using System;
using System.Collections.Generic;
using UnityEditor;
using YooAsset.Editor;

namespace EasyFramework.Editor
{
    /// <summary>通过 YooAsset 收集器读取包内可寻址资源的实际映射。</summary>
    [InitializeOnLoad]
    internal static class YooAssetEditorPackageAddresses
    {
        static YooAssetEditorPackageAddresses()
        {
            EditorPackageAddressProviders.Register<YooAssetPackageConfig>(Collect);
            EditorPackageAddressProviders.RegisterBuildOutput<YooAssetPackageConfig>(YooAssetBuildOutputValidation.Validate);
            EditorPackageAddressProviders.RegisterOfflinePackage<YooAssetPackageConfig>(YooAssetBuildOutputValidation.ValidateOffline);
        }

        private static IReadOnlyDictionary<string, string> Collect(YooAssetPackageConfig package)
        {
            string name = package.PackageName;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"资源包 {package.Alias} 缺少 YooAsset 包名。");
            if (!BundleCollectorSettingData.HasSettingAsset())
                throw new InvalidOperationException($"YooAsset 包 {name} 缺少资源收集器配置。");

            CollectResult result = BundleCollectorSettingData.Setting.BeginCollect(name, false, false, true);
            // 按收集配置的大小写规则识别地址冲突，保留工程资产路径原样。
            var addresses = new Dictionary<string, string>(
                result.Command.LocationToLower ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (CollectAssetInfo asset in result.CollectAssets)
            {
                if (asset.CollectorType != ECollectorType.MainAssetCollector || string.IsNullOrWhiteSpace(asset.Address))
                    continue;

                string path = asset.AssetInfo.AssetPath;
                if (!addresses.TryAdd(asset.Address, path))
                    throw new InvalidOperationException($"YooAsset 包 {name} 的地址 {asset.Address} 重复：{addresses[asset.Address]} 与 {path}。");
            }
            return addresses;
        }
    }
}
