using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using YooAsset;

namespace EasyFramework.Editor
{
    /// <summary>校验 YooAsset 的待上传产物及随 Player 安装的离线内置包。</summary>
    internal static class YooAssetBuildOutputValidation
    {
        private static readonly StringComparison _pathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        #region 公共方法

        internal static string Validate(YooAssetPackageConfig package, string version, string outputDirectory,
            IReadOnlyList<string> requiredAddresses, IReadOnlyDictionary<string, string> expectedAssetPaths,
            IReadOnlyDictionary<string, string> expectedBinaryDigests)
        {
            string packageName = package.PackageName;
            RequirePathPart(packageName, "YooAsset 包名");
            RequirePathPart(version, "清单版本");
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                throw new DirectoryNotFoundException("请选择存在的 YooAsset 待上传目录。");

            var sdk = new ManifestSdk();
            string manifestName = YooAssetConfiguration.GetManifestBinaryFileName(packageName, version);
            string hashName = YooAssetConfiguration.GetPackageHashFileName(packageName, version);
            string directory = FindPackageDirectory(Path.GetFullPath(outputDirectory), packageName, version,
                manifestName, hashName);
            string manifestPath = GetPackageFile(directory, manifestName);
            string hashPath = GetPackageFile(directory, hashName);

            object manifest = sdk.ReadManifest(manifestPath, hashPath);
            VerifyManifestIdentity(manifest, manifestPath, packageName, version);

            IList bundles = GetField<IList>(manifest, "BundleList");
            HashSet<int> startupBundles = CollectStartupBundles(manifest, bundles, requiredAddresses, expectedAssetPaths);
            for (int i = 0; i < bundles.Count; i++)
                VerifyBundleFile(directory, bundles[i], sdk.GetBundleName(bundles[i]), startupBundles.Contains(i));
            VerifyPackedBinaries(directory, sdk, manifest, bundles, expectedBinaryDigests);

            string report = $"出包目录 {directory}，已校验清单及 {bundles.Count} 个 Bundle 的存在与大小，"
                            + $"其中 {startupBundles.Count} 个启动依赖 Bundle 已核对 CRC。";
            if (expectedBinaryDigests.Count > 0)
                report += $"已读取实际 Bundle 中的 {expectedBinaryDigests.Count} 个 DLL 或 AOT 元数据，SHA-256 均与发行一致。";
            return report;
        }

        /// <summary>核对指定 Player 构建会包含的原生离线包。</summary>
        /// <param name="package">运行时使用默认内置文件系统的资源包。</param>
        /// <param name="pinnedVersion">离线发行锁定的版本。未锁定时使用已安装版本。</param>
        /// <param name="requiredAddresses">必须随包提供的资源地址。</param>
        /// <param name="target">当前预检的 Player 构建目标。</param>
        /// <returns>内置包验证结果或 WebGL 检查跳过说明。</returns>
        internal static string ValidateOffline(YooAssetPackageConfig package, string pinnedVersion,
            IReadOnlyList<string> requiredAddresses, BuildTarget target)
        {
            string packageName = package.PackageName;
            if (target == BuildTarget.WebGL)
                return $"YooAsset 包「{packageName}」在 WebGL 使用 Web 文件系统，已跳过原生离线内置包检查。";

            RequirePathPart(packageName, "YooAsset 包名");
            string directory = Path.Combine(Application.streamingAssetsPath, YooAssetConfiguration.GetYooFolderName() ?? string.Empty, packageName);
            string versionPath = GetPackageFile(directory, YooAssetConfiguration.GetPackageVersionFileName(packageName));
            if (!File.Exists(versionPath))
                throw new FileNotFoundException($"缺少 YooAsset 内置包版本文件「{versionPath}」。请使用 {target} 构建该资源包并复制到 StreamingAssets。", versionPath);

            // 内置文件系统按版本文件的原文本查找清单，不会裁剪空白。
            string version = File.ReadAllText(versionPath);
            RequirePathPart(version, $"内置包版本文件「{versionPath}」中的清单版本");
            if (!string.IsNullOrEmpty(pinnedVersion) && version != pinnedVersion)
                throw new InvalidDataException($"YooAsset 内置包「{packageName}」安装了版本「{version}」，离线发行锁定了版本「{pinnedVersion}」。请重新复制锁定版本的内置包。");

            var sdk = new ManifestSdk();
            string manifestPath = GetPackageFile(directory, YooAssetConfiguration.GetManifestBinaryFileName(packageName, version));
            string hashPath = GetPackageFile(directory, YooAssetConfiguration.GetPackageHashFileName(packageName, version));
            object manifest = sdk.ReadManifest(manifestPath, hashPath);
            VerifyManifestIdentity(manifest, manifestPath, packageName, version);
            Dictionary<string, string> catalog = sdk.ReadCatalog(directory, packageName, version);
            IList bundles = GetField<IList>(manifest, "BundleList");
            HashSet<int> requiredBundles = CollectStartupBundles(manifest, bundles, requiredAddresses, null);

            int installedBundles = 0;
            for (int i = 0; i < bundles.Count; i++)
            {
                object bundle = bundles[i];
                string guid = sdk.GetBundleGuid(bundle);
                bool isRequired = requiredBundles.Contains(i);
                bool hasCatalogEntry = catalog.TryGetValue(guid, out string catalogFileName);
                // 首包允许按标签复制，Catalog 声明已安装的 Bundle 和必需依赖都要校验。
                if (!isRequired && !hasCatalogEntry) continue;

                string fileName = sdk.GetBundleName(bundle);
                if (!hasCatalogEntry || catalogFileName != fileName)
                    throw new InvalidDataException($"YooAsset 内置目录「{directory}」的 Catalog 缺少 Bundle「{fileName}」的正确映射。请重新复制内置包并生成 Catalog。");
                VerifyBundleFile(directory, bundle, fileName, isRequired);
                installedBundles++;
            }

            return $"YooAsset 内置包「{packageName}」版本「{version}」已核对版本文件、清单、Catalog 和 {installedBundles} 个内置 Bundle，"
                   + $"其中 {requiredBundles.Count} 个必需 Bundle 已核对 CRC。";
        }

        #endregion

        #region 私有逻辑

        private static string FindPackageDirectory(string root, string packageName, string version,
            string manifestName, string hashName)
        {
            var candidates = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            AddIfContainsManifest(root);
            AddIfContainsManifest(Path.Combine(root, version));
            AddIfContainsManifest(Path.Combine(root, packageName));
            AddIfContainsManifest(Path.Combine(root, packageName, version));
            // YooAsset 的默认构建目录为 <根>/<平台>/<包名>/<版本>。
            foreach (string platformDirectory in Directory.EnumerateDirectories(root))
                AddIfContainsManifest(Path.Combine(platformDirectory, packageName, version));
            if (candidates.Count == 0)
                throw new FileNotFoundException($"在「{root}」及 YooAsset 的标准子目录中找不到「{manifestName}」。");
            if (candidates.Count > 1)
                throw new InvalidOperationException($"找到多个「{manifestName}」，请选择明确的待上传目录。{string.Join("、", candidates)}");
            foreach (string candidate in candidates) return candidate;
            throw new InvalidOperationException("无法定位 YooAsset 出包目录。");

            void AddIfContainsManifest(string location)
            {
                if (File.Exists(GetPackageFile(location, manifestName)) || File.Exists(GetPackageFile(location, hashName)))
                    candidates.Add(Path.GetFullPath(location));
            }
        }

        private static HashSet<int> CollectStartupBundles(object manifest, IList bundles,
            IEnumerable<string> requiredAddresses, IReadOnlyDictionary<string, string> expectedAssetPaths)
        {
            var required = new HashSet<int>();
            var pending = new Stack<int>();
            IDictionary locations = GetField<IDictionary>(manifest, "AssetsByLocation");
            bool isPathReplaced = GetField<bool>(manifest, "ReplaceAssetPathWithAddress");
            bool hasLowercaseLocations = GetField<bool>(manifest, "LocationToLower");

            foreach (string address in requiredAddresses)
            {
                object asset = FindManifestAsset(locations, address, hasLowercaseLocations);
                if (expectedAssetPaths != null && expectedAssetPaths.TryGetValue(address, out string expectedPath))
                    VerifyAssetPath(asset, address, expectedPath, isPathReplaced);
                pending.Push(GetField<int>(asset, "BundleID"));
                AddDependencies(asset);
            }

            while (pending.Count > 0)
            {
                int index = pending.Pop();
                if (index < 0 || index >= bundles.Count)
                    throw new InvalidDataException($"清单引用了无效的 Bundle 编号 {index}。");
                if (required.Add(index)) AddDependencies(bundles[index]);
            }
            return required;

            void AddDependencies(object assetOrBundle)
            {
                foreach (int index in GetField<int[]>(assetOrBundle, "DependentBundleIDs"))
                    pending.Push(index);
            }
        }

        private static object FindManifestAsset(IDictionary locations, string address, bool hasLowercaseLocations)
        {
            string location = hasLowercaseLocations ? address.ToLowerInvariant() : address;
            return locations[location]
                   ?? throw new InvalidDataException($"实际清单缺少启动地址「{address}」，请重新收集并构建。");
        }

        private static void VerifyPackedBinaries(string directory, ManifestSdk sdk, object manifest, IList bundles,
            IReadOnlyDictionary<string, string> expectedDigests)
        {
            if (expectedDigests.Count == 0) return;
            var bundleType = (EBundleType)GetField<int>(manifest, "BuildBundleType");
            if (bundleType != EBundleType.AssetBundle)
                throw new InvalidDataException($"包类型 {bundleType} 无法提供热更入口所需的 TextAsset。请用 Unity AssetBundle 构建热更 DLL 和 AOT 元数据。");

            HashSet<int> binaryBundles = CollectStartupBundles(manifest, bundles, expectedDigests.Keys, null);
            var loadedBundles = new Dictionary<int, AssetBundle>();
            var previousBundles = new HashSet<AssetBundle>(AssetBundle.GetAllLoadedAssetBundles());
            try
            {
                // 先打开字节资产所在包及其依赖，再按清单中的实际加载键读取 TextAsset。
                foreach (int index in binaryBundles)
                {
                    object bundle = bundles[index];
                    string path = GetPackageFile(directory, sdk.GetBundleName(bundle));
                    if (GetField<bool>(bundle, "IsEncrypted"))
                        throw new InvalidDataException($"热更字节依赖加密 Bundle「{path}」，当前预检无法读取其明文。请将热更字节及其依赖放入可直接读取的 AssetBundle。");
                    AssetBundle loadedBundle = AssetBundle.LoadFromFile(path);
                    if (loadedBundle == null || previousBundles.Contains(loadedBundle))
                        throw new InvalidDataException($"无法从待上传目录打开 Bundle「{path}」。请检查构建产物及 Unity 日志，并释放编辑器中占用的同名 Bundle 后重试。");
                    loadedBundles.Add(index, loadedBundle);
                }

                IDictionary locations = GetField<IDictionary>(manifest, "AssetsByLocation");
                bool hasLowercaseLocations = GetField<bool>(manifest, "LocationToLower");
                using SHA256 sha = SHA256.Create();
                foreach (var pair in expectedDigests)
                {
                    object asset = FindManifestAsset(locations, pair.Key, hasLowercaseLocations);
                    int bundleIndex = GetField<int>(asset, "BundleID");
                    string assetPath = GetField<string>(asset, "AssetPath");
                    string fileName = sdk.GetBundleName(bundles[bundleIndex]);
                    byte[] bytes = loadedBundles[bundleIndex].LoadAsset<TextAsset>(assetPath)?.bytes;
                    if (bytes == null || bytes.Length == 0)
                        throw new InvalidDataException($"实际 Bundle「{fileName}」中的「{pair.Key}」无法读为非空 TextAsset。请核对资源收集规则并重新构建。");

                    string digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
                    if (!string.Equals(digest, pair.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"实际 Bundle「{fileName}」中的「{pair.Key}」SHA-256 不匹配。期望 {pair.Value}，实际 {digest.ToLowerInvariant()}。请重新构建待上传包。");
                }
            }
            finally
            {
                foreach (AssetBundle bundle in loadedBundles.Values)
                    if (bundle != null) bundle.Unload(unloadAllLoadedObjects: true);
            }
        }

        private static void VerifyAssetPath(object asset, string address, string expectedPath, bool isPathReplaced)
        {
            string path = GetField<string>(asset, "AssetPath");
            if (path == expectedPath) return;
            if (isPathReplaced && path == GetField<string>(asset, "Address"))
            {
                // 用地址代替路径的清单可通过 GUID 对照工程资产。若清单省略了 GUID，只能核对地址与 Bundle。
                string guid = GetField<string>(asset, "AssetGuid");
                if (string.IsNullOrEmpty(guid) ||
                    string.Equals(AssetDatabase.AssetPathToGUID(expectedPath), guid, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            throw new InvalidDataException($"实际清单的地址「{address}」指向「{path}」，与收集器中的「{expectedPath}」不一致。");
        }

        private static void VerifyManifestIdentity(object manifest, string path, string packageName, string version)
        {
            string actualPackage = GetField<string>(manifest, "PackageName");
            string actualVersion = GetField<string>(manifest, "PackageVersion");
            if (actualPackage != packageName || actualVersion != version)
                throw new InvalidDataException($"清单「{path}」内记录为「{actualPackage}」版本「{actualVersion}」，期望「{packageName}」版本「{version}」。");
            if (GetField<int>(manifest, "BuildBundleType") == (int)EBundleType.VirtualAssetBundle)
                throw new InvalidDataException($"清单「{path}」来自编辑器模拟构建，请使用实际出包产物。");
        }

        private static void VerifyBundleFile(string directory, object bundle, string fileName, bool hasCrcCheck)
        {
            string path = GetPackageFile(directory, fileName);
            long expectedSize = GetField<long>(bundle, "FileSize");
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException($"资源包目录缺少 Bundle「{path}」。", path);
            if (file.Length != expectedSize)
                throw new InvalidDataException($"Bundle「{path}」大小不符，期望 {expectedSize} 字节，实际 {file.Length} 字节。");
            if (!hasCrcCheck) return;

            uint expectedCrc = GetField<uint>(bundle, "FileCrc");
            uint actualCrc = HashUtility.ComputeFileCrc32AsUInt(path);
            if (actualCrc != expectedCrc)
                throw new InvalidDataException($"必需 Bundle「{path}」的 CRC 不符，期望 {expectedCrc:x8}，实际 {actualCrc:x8}。");
        }

        private static string GetPackageFile(string directory, string relativePath)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(prefix, relativePath));
            if (!path.StartsWith(prefix, _pathComparison) || path == prefix)
                throw new InvalidDataException($"YooAsset 文件名「{relativePath}」超出所选出包目录。");
            return path;
        }

        private static void RequirePathPart(string part, string label)
        {
            if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                part.Contains("\\") || part.Contains("/") || part.Contains(":"))
                throw new InvalidDataException($"{label}「{part}」不能用于定位出包目录。");
        }

        private static T GetField<T>(object item, string fieldName)
        {
            FieldInfo field = item.GetType().GetField(fieldName);
            if (field == null || field.GetValue(item) is not T value)
                throw new InvalidDataException($"无法读取 YooAsset 清单字段「{fieldName}」，请检查文件及 SDK 版本。");
            return value;
        }

        /// <summary>调用 YooAsset 的内部清单和 Catalog 读取接口。</summary>
        private sealed class ManifestSdk
        {
            private readonly Assembly _assembly = typeof(YooAssets).Assembly;
            private readonly MethodInfo _verifyManifest;
            private readonly ConstructorInfo _readManifest;
            private readonly PropertyInfo _manifest;
            private readonly MethodInfo _bundleName;

            internal ManifestSdk()
            {
                _verifyManifest = RequireMethod("PackageManifestHelper", "VerifyManifestData", typeof(byte[]), typeof(string));
                Type operation = RequireType("DeserializeManifestOperation");
                _readManifest = operation.GetConstructor(new[] { RequireType("IManifestDecryptor"), typeof(byte[]) })
                    ?? throw new InvalidOperationException("当前 YooAsset SDK 缺少清单反序列化构造方法。");
                _manifest = operation.GetProperty("Manifest")
                    ?? throw new InvalidOperationException("当前 YooAsset SDK 缺少清单反序列化结果属性。");
                _bundleName = RequireMethod("PackageBundle", "GetFileName");
            }

            internal object ReadManifest(string path, string hashPath)
            {
                if (!File.Exists(path)) throw new FileNotFoundException($"缺少 YooAsset 二进制清单「{path}」。", path);
                if (!File.Exists(hashPath)) throw new FileNotFoundException($"缺少 YooAsset 清单校验文件「{hashPath}」。", hashPath);
                if (new FileInfo(hashPath).Length > 128)
                    throw new InvalidDataException($"YooAsset 清单校验文件「{hashPath}」格式无效。");
                byte[] bytes = File.ReadAllBytes(path);
                string hash = File.ReadAllText(hashPath);
                if (!(bool)_verifyManifest.Invoke(null, new object[] { bytes, hash }))
                    throw new InvalidDataException($"二进制清单「{path}」与校验文件「{hashPath}」不匹配。");
                try
                {
                    // SDK 会把解析错误保存在操作状态中，失败的操作可能已经创建了部分清单。
                    var operation = (AsyncOperationBase)_readManifest.Invoke(new object[] { null, bytes });
                    operation.WaitForCompletion();
                    if (operation.Status != EOperationStatus.Succeeded)
                        throw new InvalidDataException(operation.Error);
                    return _manifest.GetValue(operation) ?? throw new InvalidDataException("SDK 未返回清单对象。");
                }
                catch (Exception exception)
                {
                    Exception error = exception.GetBaseException();
                    throw new InvalidDataException($"无法读取二进制清单「{path}」。请检查 SDK 版本与构建选项，"
                        + $"当前预检需要可直接读取的清单。{error.Message}", error);
                }
            }

            internal string GetBundleName(object bundle) => (string)_bundleName.Invoke(bundle, null);

            internal string GetBundleGuid(object bundle) => bundle.GetType().GetProperty("BundleGuid")?.GetValue(bundle) as string
                ?? throw new InvalidDataException("无法读取 YooAsset Bundle 的标识，请检查清单及 SDK 版本。");

            internal Dictionary<string, string> ReadCatalog(string directory, string packageName, string version)
            {
                string fileName = RequireType("BuiltinCatalogConsts").GetField("BinaryFileName")?.GetRawConstantValue() as string
                    ?? throw new InvalidOperationException("无法读取 YooAsset 的内置 Catalog 文件名，请检查 SDK 版本。");
                string path = GetPackageFile(directory, fileName);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"缺少 YooAsset 内置 Catalog「{path}」，请使用 YooAsset 重新复制内置包。", path);

                MethodInfo read = RequireMethod("BuiltinCatalogHelper", "DeserializeFromBinary", typeof(byte[]));
                object catalog;
                try
                {
                    catalog = read.Invoke(null, new object[] { File.ReadAllBytes(path) })
                              ?? throw new InvalidDataException("SDK 未返回 Catalog 对象。");
                }
                catch (Exception exception)
                {
                    string error = exception.GetBaseException().Message;
                    throw new InvalidDataException($"无法读取 YooAsset 内置 Catalog「{path}」，{error}", exception);
                }

                string actualPackage = GetField<string>(catalog, "PackageName");
                string actualVersion = GetField<string>(catalog, "PackageVersion");
                if (actualPackage != packageName || actualVersion != version)
                    throw new InvalidDataException($"内置 Catalog「{path}」内记录为「{actualPackage}」版本「{actualVersion}」，期望「{packageName}」版本「{version}」。");

                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object entry in GetField<IList>(catalog, "Entries"))
                {
                    string guid = GetField<string>(entry, "BundleGuid");
                    string bundleName = GetField<string>(entry, "FileName");
                    if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(bundleName) || !entries.TryAdd(guid, bundleName))
                        throw new InvalidDataException($"内置 Catalog「{path}」包含无效或重复的 Bundle 条目「{guid}」。");
                }
                return entries;
            }

            private Type RequireType(string name) => _assembly.GetType("YooAsset." + name)
                ?? throw new InvalidOperationException($"当前 YooAsset SDK 缺少清单预检所需类型「{name}」。");

            private MethodInfo RequireMethod(string type, string name, params Type[] parameters) =>
                RequireType(type).GetMethod(name, BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null)
                ?? throw new InvalidOperationException($"当前 YooAsset SDK 缺少清单预检所需方法「{type}.{name}」。");
        }

        #endregion
    }
}
