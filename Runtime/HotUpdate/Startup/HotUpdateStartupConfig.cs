using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>绑定指定 Player 基线和平台的一套资源版本、字节产物及热更入口。</summary>
    [CreateAssetMenu(fileName = "HotUpdateStartup", menuName = "EasyFramework/Hot Update Startup")]
    public sealed class HotUpdateStartupConfig : ScriptableObject
    {
        /// <summary>发行选定的资源包版本与入口前需要下载的地址。</summary>
        [Serializable]
        private sealed class PackageVersion
        {
            [SerializeField] private string _alias = AssetModule.MainPackage;
            [SerializeField] private string _version;
            [SerializeField] private string[] _startupAddresses = Array.Empty<string>();

            internal string Alias => _alias?.Trim();
            internal string Version => _version?.Trim();
            internal IReadOnlyList<string> StartupAddresses => _startupAddresses ?? Array.Empty<string>();
        }

        /// <summary>声明字节资产的地址、程序集名和来自构建产物的摘要。</summary>
        [Serializable]
        private sealed class Binary
        {
            [SerializeField] private string _address;
            [SerializeField] private string _assemblyName;
            [SerializeField] private string _sha256;
            [SerializeField, Tooltip("仅热更 DLL 使用。依赖的热更程序集须先出现在列表中。")]
            private string[] _dependencies = Array.Empty<string>();

            internal string Address => _address?.Trim();
            internal string AssemblyName => _assemblyName?.Trim();
            internal string Sha256 => _sha256?.Trim();
            internal IReadOnlyList<string> Dependencies => _dependencies ?? Array.Empty<string>();
        }

        private static readonly Regex _assemblyPattern = new(@"\A[A-Za-z0-9_.-]+\z");
        private static readonly Regex _shaPattern = new(@"\A[0-9a-fA-F]{64}\z");

        [SerializeField] private string _releaseId;
        [SerializeField] private string _requiredPlayerBaselineId;
        [SerializeField] private RuntimePlatform _targetPlatform = RuntimePlatform.WindowsPlayer;
        [SerializeField] private PackageVersion[] _packages = Array.Empty<PackageVersion>();
        [SerializeField] private HotUpdateRuntimeProvider _runtimeProvider;
        [SerializeField] private Binary[] _aotMetadata = Array.Empty<Binary>();
        [SerializeField] private Binary[] _hotfixAssemblies = Array.Empty<Binary>();
        [SerializeField] private string _entryAssembly;
        [SerializeField] private string _entryType;
        [SerializeField] private string _entryMethod = "Start";

        /// <summary>本组资源和代码的发行标识。</summary>
        public string ReleaseId => _releaseId?.Trim();
        /// <summary>发行所对应的 Player 平台。</summary>
        public RuntimePlatform TargetPlatform => _targetPlatform;

        #region 公共方法

        /// <summary>检查指定 Player 与发行配置的兼容性及所有启动声明。</summary>
        /// <param name="startup">提供 Player 基线、包和运行方式的启动配置。</param>
        /// <param name="targetPlatform">所校验的 Player 平台。</param>
        public void Validate(FrameworkStartupConfig startup, RuntimePlatform targetPlatform) =>
            BuildPlan(startup, targetPlatform);

        /// <summary>将固定配置的资源与入口声明导出为独立发行记录。</summary>
        /// <param name="buildKind">发行对应的 Player 构建类型。</param>
        /// <returns>不包含本地运行时适配器的发行快照。</returns>
        public HotUpdateReleaseRecord CreateReleaseRecord(HotUpdateBuildKind buildKind)
        {
            PackageVersion[] source = _packages ?? Array.Empty<PackageVersion>();
            var packages = new HotUpdateReleaseRecord.PackageVersion[source.Length];
            for (int i = 0; i < packages.Length; i++)
            {
                PackageVersion package = source[i];
                if (package != null)
                    packages[i] = new HotUpdateReleaseRecord.PackageVersion(package.Alias, package.Version,
                        package.StartupAddresses);
            }

            return new HotUpdateReleaseRecord(ReleaseId, _requiredPlayerBaselineId?.Trim(), _targetPlatform,
                buildKind, packages, CaptureBinaries(_aotMetadata), CaptureBinaries(_hotfixAssemblies),
                _entryAssembly?.Trim(), _entryType?.Trim(), _entryMethod?.Trim());
        }

        #endregion

        #region 私有逻辑

        internal HotUpdatePlan BuildPlan(FrameworkStartupConfig startup, RuntimePlatform targetPlatform) =>
            BuildPlan(startup, targetPlatform, HotUpdateBuildKind.Any, CreateReleaseRecord(HotUpdateBuildKind.Any));

        internal HotUpdatePlan BuildPlan(FrameworkStartupConfig startup, RuntimePlatform targetPlatform,
            HotUpdateBuildKind buildKind, HotUpdateReleaseRecord release)
        {
            if (startup == null) throw new ArgumentNullException(nameof(startup));
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (string.IsNullOrWhiteSpace(release.ReleaseId)) throw new InvalidOperationException("请填写发行 ID。");
            string requiredBaseline = release.PlayerBaselineId;
            if (string.IsNullOrEmpty(requiredBaseline) || string.IsNullOrEmpty(startup.PlayerBaselineId) ||
                !string.Equals(requiredBaseline, startup.PlayerBaselineId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Player 基线不匹配。发行要求「{requiredBaseline}」，当前为「{startup.PlayerBaselineId}」。");
            if (release.TargetPlatform != targetPlatform)
                throw new InvalidOperationException($"Player 平台不匹配。发行要求 {release.TargetPlatform}，当前为 {targetPlatform}。");
            if (release.BuildKind != buildKind)
                throw new InvalidOperationException($"Player 构建类型不匹配。发行要求 {release.BuildKind}，当前为 {buildKind}。");
            if (_runtimeProvider == null) throw new InvalidOperationException("请配置热更运行时适配器。");

            Dictionary<string, HotUpdatePlan.Package> packages = ReadPackages(startup, release);
            var usedAddresses = new HashSet<string>(StringComparer.Ordinal);
            HotUpdatePlan.Binary[] aot = ReadBinaries(release.AotMetadataCount, release.GetAotMetadata,
                "AOT 元数据", packages, usedAddresses, false);
            HotUpdatePlan.Binary[] assemblies = ReadBinaries(release.AssemblyCount, release.GetAssembly,
                "热更 DLL", packages, usedAddresses, true);
            if (assemblies.Length == 0) throw new InvalidOperationException("请登记至少一个热更 DLL。");
            string entryAssembly = release.EntryAssembly;
            if (Array.FindIndex(assemblies, binary => binary.AssemblyName == entryAssembly) < 0 ||
                string.IsNullOrEmpty(release.EntryType) || string.IsNullOrEmpty(release.EntryMethod))
                throw new InvalidOperationException("请填写已登记的入口程序集、入口类型全名和入口方法。");

            _runtimeProvider.ValidateConfiguration(aot.Length > 0);
            return new HotUpdatePlan(release.ReleaseId, new List<HotUpdatePlan.Package>(packages.Values), aot, assemblies,
                _runtimeProvider, entryAssembly, release.EntryType, release.EntryMethod);
        }

        private static Dictionary<string, HotUpdatePlan.Package> ReadPackages(FrameworkStartupConfig startup,
            HotUpdateReleaseRecord release)
        {
            var configured = new Dictionary<string, IHotUpdatePackageConfig>(StringComparer.Ordinal);
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            var yooNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AssetPackageConfig package in startup.AssetPackages)
            {
                if (package == null) throw new InvalidOperationException("Startup Config 中有空的资源包。");
                string alias = AssetModule.NormalizeAlias(package.Alias);
                if (!aliases.Add(alias)) throw new InvalidOperationException($"包别名「{alias}」重复。");
                if (!(package is IHotUpdatePackageConfig backend)) continue;
                if (package is IYooAssetPackageConfig yoo && !yooNames.Add(yoo.PackageName))
                    throw new InvalidOperationException($"YooAsset 包名「{yoo.PackageName}」重复。");
                configured.Add(alias, backend);
            }

            var packages = new Dictionary<string, HotUpdatePlan.Package>(StringComparer.Ordinal);
            for (int i = 0; i < release.PackageCount; i++)
            {
                HotUpdateReleaseRecord.PackageVersion version = release.GetPackage(i);
                if (string.IsNullOrEmpty(version.Alias))
                    throw new InvalidOperationException("请填写发行中每个受管资源包的别名。");
                if (!configured.TryGetValue(version.Alias, out IHotUpdatePackageConfig backend))
                    throw new InvalidOperationException($"「{version.Alias}」未在 Startup Config 登记为可更新包。");
                if (string.IsNullOrEmpty(version.Version))
                    throw new InvalidOperationException($"包「{version.Alias}」缺少清单版本。");
                if (packages.ContainsKey(version.Alias))
                    throw new InvalidOperationException($"发行中包「{version.Alias}」重复。");

                try { backend.ValidateHotUpdateConfiguration(startup.RuntimeMode, version.Version); }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"包「{version.Alias}」配置无效。{exception.Message}", exception);
                }

                var package = new HotUpdatePlan.Package(version.Alias, version.Version, backend);
                foreach (string address in version.StartupAddresses)
                {
                    if (string.IsNullOrWhiteSpace(address) || address.IndexOf(':') >= 0)
                        throw new InvalidOperationException($"包「{version.Alias}」的启动地址应填写有效的包内地址。");
                    package.AddRequiredAddress(address.Trim());
                }
                packages.Add(version.Alias, package);
            }

            foreach (string alias in configured.Keys)
                if (!packages.ContainsKey(alias)) throw new InvalidOperationException($"发行缺少包「{alias}」的清单版本。");
            if (packages.Count == 0) throw new InvalidOperationException("请配置至少一个锁定版本的资源包。");
            return packages;
        }

        private static HotUpdatePlan.Binary[] ReadBinaries(int count, Func<int, HotUpdateReleaseRecord.Binary> readBinary, string group,
            Dictionary<string, HotUpdatePlan.Package> packages, HashSet<string> usedAddresses, bool hasDependencyOrder)
        {
            var binaries = new HotUpdatePlan.Binary[count];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                HotUpdateReleaseRecord.Binary config = readBinary(i);
                if (string.IsNullOrEmpty(config.AssemblyName) ||
                    !_assemblyPattern.IsMatch(config.AssemblyName) ||
                    config.AssemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{group}第 {i + 1} 项需填写程序集短名称，省略 .dll。");
                if (string.IsNullOrEmpty(config.Sha256) || !_shaPattern.IsMatch(config.Sha256))
                    throw new InvalidOperationException($"{group}「{config.AssemblyName}」需填写 64 位十六进制 SHA-256。");

                string address = config.Address;
                int delimiter = address?.IndexOf(':') ?? -1;
                if (delimiter <= 0 || address.IndexOf(':', delimiter + 1) >= 0 ||
                    string.IsNullOrWhiteSpace(address.Substring(delimiter + 1)) ||
                    !packages.TryGetValue(address.Substring(0, delimiter), out HotUpdatePlan.Package package))
                    throw new InvalidOperationException($"{group}「{config.AssemblyName}」需引用受管包地址（包别名:地址）。");
                string location = address.Substring(delimiter + 1).Trim();
                if (!usedAddresses.Add($"{package.Alias}:{location}"))
                    throw new InvalidOperationException($"字节地址「{address}」重复。");

                if (hasDependencyOrder)
                    foreach (string dependency in config.Dependencies)
                        if (string.IsNullOrWhiteSpace(dependency) || !names.Contains(dependency.Trim()))
                            throw new InvalidOperationException(
                                $"热更 DLL「{config.AssemblyName}」的依赖「{dependency}」需先登记在 DLL 列表中。");
                if (!names.Add(config.AssemblyName))
                    throw new InvalidOperationException($"{group}程序集「{config.AssemblyName}」重复。");

                package.AddRequiredAddress(location);
                binaries[i] = new HotUpdatePlan.Binary(package.Alias, location, config.AssemblyName, config.Sha256);
            }
            return binaries;
        }

        private static HotUpdateReleaseRecord.Binary[] CaptureBinaries(Binary[] source)
        {
            source ??= Array.Empty<Binary>();
            var result = new HotUpdateReleaseRecord.Binary[source.Length];
            for (int i = 0; i < result.Length; i++)
            {
                Binary binary = source[i];
                if (binary != null)
                    result[i] = new HotUpdateReleaseRecord.Binary(binary.Address, binary.AssemblyName, binary.Sha256,
                        binary.Dependencies);
            }
            return result;
        }

        #endregion
    }

    /// <summary>完整校验后固定的单次启动发行计划。</summary>
    internal sealed class HotUpdatePlan
    {
        /// <summary>指定资源包的后端、清单版本与所需地址。</summary>
        internal sealed class Package
        {
            private readonly List<string> _requiredAddresses = new();

            internal string Alias { get; }
            internal string Version { get; }
            internal IHotUpdatePackageConfig Backend { get; }
            internal IReadOnlyList<string> RequiredAddresses => _requiredAddresses;

            internal Package(string alias, string version, IHotUpdatePackageConfig backend)
            {
                Alias = alias;
                Version = version;
                Backend = backend;
            }

            internal void AddRequiredAddress(string address)
            {
                if (!_requiredAddresses.Contains(address)) _requiredAddresses.Add(address);
            }
        }

        /// <summary>需要在进程装载前校验的字节资产。</summary>
        internal readonly struct Binary
        {
            internal string Alias { get; }
            internal string Location { get; }
            internal string AssemblyName { get; }
            internal string Sha256 { get; }
            internal string Key => $"{Alias}:{Location}";

            internal Binary(string alias, string location, string assemblyName, string sha256)
            {
                Alias = alias;
                Location = location;
                AssemblyName = assemblyName;
                Sha256 = sha256;
            }
        }

        internal string ReleaseId { get; }
        internal IReadOnlyList<Package> Packages { get; }
        internal IReadOnlyList<Binary> AotMetadata { get; }
        internal IReadOnlyList<Binary> Assemblies { get; }
        internal HotUpdateRuntimeProvider RuntimeProvider { get; }
        internal string EntryAssembly { get; }
        internal string EntryType { get; }
        internal string EntryMethod { get; }

        internal HotUpdatePlan(string releaseId, IReadOnlyList<Package> packages, IReadOnlyList<Binary> aot,
            IReadOnlyList<Binary> assemblies, HotUpdateRuntimeProvider runtimeProvider,
            string entryAssembly, string entryType, string entryMethod)
        {
            ReleaseId = releaseId;
            Packages = packages;
            AotMetadata = aot;
            Assemblies = assemblies;
            RuntimeProvider = runtimeProvider;
            EntryAssembly = entryAssembly;
            EntryType = entryType;
            EntryMethod = entryMethod;
        }
    }
}
