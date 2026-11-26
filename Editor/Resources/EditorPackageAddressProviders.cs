using System;
using System.Collections.Generic;
using UnityEditor;

namespace EasyFramework.Editor
{
    /// <summary>登记可选资源后端的编辑期地址收集器与产物校验器。</summary>
    public static class EditorPackageAddressProviders
    {
        private static readonly Dictionary<Type, Func<AssetPackageConfig, IReadOnlyDictionary<string, string>>> _providers = new();
        private static readonly Dictionary<Type, Func<AssetPackageConfig, string, string, IReadOnlyList<string>,
            IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>, string>> _buildOutputProviders = new();
        private static readonly Dictionary<Type, Func<AssetPackageConfig, string, IReadOnlyList<string>, BuildTarget, string>>
            _offlinePackageProviders = new();

        /// <summary>为一个资源配置类型登记包内地址到工程资产路径的收集器。</summary>
        /// <typeparam name="TPackage">适配器支持的资源包配置类型。</typeparam>
        /// <param name="collect">读取收集配置并返回包内地址和资产路径的函数。</param>
        public static void Register<TPackage>(Func<TPackage, IReadOnlyDictionary<string, string>> collect)
            where TPackage : AssetPackageConfig
        {
            if (collect == null) throw new ArgumentNullException(nameof(collect));
            if (!_providers.TryAdd(typeof(TPackage), package => collect((TPackage)package)))
                throw new InvalidOperationException($"资源包配置 {typeof(TPackage).Name} 的编辑期收集器已登记。");
        }

        /// <summary>为一种资源包配置登记待发布产物的校验器。</summary>
        /// <typeparam name="TPackage">适配器支持的资源包配置类型。</typeparam>
        /// <param name="validate">接收包配置、版本、待上传目录、必需地址、收集映射及包内地址对应的 SHA-256，核对实际产物并返回说明。无字节摘要时该映射为空。</param>
        public static void RegisterBuildOutput<TPackage>(Func<TPackage, string, string,
            IReadOnlyList<string>, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>, string> validate)
            where TPackage : AssetPackageConfig
        {
            if (validate == null) throw new ArgumentNullException(nameof(validate));
            if (!_buildOutputProviders.TryAdd(typeof(TPackage), (package, version, directory, addresses, assetPaths, digests) =>
                    validate((TPackage)package, version, directory, addresses, assetPaths, digests)))
                throw new InvalidOperationException($"资源包配置 {typeof(TPackage).Name} 的产物校验器已登记。");
        }

        /// <summary>为一种资源包配置登记随 Player 安装的离线产物校验器。</summary>
        /// <typeparam name="TPackage">适配器支持的资源包配置类型。</typeparam>
        /// <param name="validate">接收包配置、锁定版本（未锁定时为空）、包内必需地址和构建目标，返回校验说明。</param>
        public static void RegisterOfflinePackage<TPackage>(
            Func<TPackage, string, IReadOnlyList<string>, BuildTarget, string> validate)
            where TPackage : AssetPackageConfig
        {
            if (validate == null) throw new ArgumentNullException(nameof(validate));
            if (!_offlinePackageProviders.TryAdd(typeof(TPackage), (package, version, addresses, target) =>
                    validate((TPackage)package, version, addresses, target)))
                throw new InvalidOperationException($"资源包配置 {typeof(TPackage).Name} 的离线产物校验器已登记。");
        }

        internal static string ValidateOfflinePackage(AssetPackageConfig package, string version,
            IReadOnlyList<string> requiredAddresses, BuildTarget target)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (_offlinePackageProviders.TryGetValue(package.GetType(), out var validate))
                return validate(package, version, requiredAddresses, target);
            if (package is IYooAssetPackageConfig)
                throw new InvalidOperationException($"资源包 {package.Alias} 缺少 YooAsset 离线产物校验器，请检查编辑器适配器。");
            return null;
        }

        internal static string ValidateBuildOutput(AssetPackageConfig package, string version, string outputDirectory,
            IReadOnlyList<string> requiredAddresses, IReadOnlyDictionary<string, string> expectedAssetPaths,
            IReadOnlyDictionary<string, string> expectedBinaryDigests)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (!_buildOutputProviders.TryGetValue(package.GetType(), out var validate))
                throw new InvalidOperationException($"资源包 {package.Alias} 尚未登记编辑期产物校验器，请检查对应的资源适配器。");
            return validate(package, version, outputDirectory, requiredAddresses, expectedAssetPaths, expectedBinaryDigests);
        }

        internal static IReadOnlyDictionary<string, string> Collect(AssetPackageConfig package)
        {
            if (_providers.TryGetValue(package.GetType(), out var collect))
                return collect(package) ?? throw new InvalidOperationException($"资源包 {package.Alias} 的收集器没有返回地址。");

            if (package is IYooAssetPackageConfig)
                throw new InvalidOperationException($"资源包 {package.Alias} 缺少 YooAsset 编辑器适配器，请检查 SDK 版本及适配程序集。");
            throw new InvalidOperationException($"资源包 {package.Alias} 的配置类型 {package.GetType().Name} 尚未登记编辑期收集器。");
        }
    }
}
