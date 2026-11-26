using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>区分发行支持的 Player 构建类型。</summary>
    public enum HotUpdateBuildKind
    {
        /// <summary>使用随包固定配置时，兼容任一种构建类型。</summary>
        Any = 0,
        /// <summary>正式构建。</summary>
        Release = 1,
        /// <summary>开发构建。</summary>
        Development = 2,
    }

    /// <summary>绑定一整套资源、代码和入口的发行记录，可通过 JSON 发布或持久化。</summary>
    [Serializable]
    public sealed class HotUpdateReleaseRecord
    {
        private const int SCHEMA_VERSION = 1;
        private const int ENTRY_CONTRACT_VERSION = 1;

        /// <summary>发行 JSON 的固定协议字段。</summary>
        [Serializable]
        private sealed class Data
        {
            public int schemaVersion;
            public string releaseId;
            public string playerBaselineId;
            public RuntimePlatform targetPlatform;
            public HotUpdateBuildKind buildKind;
            public int entryContractVersion;
            public PackageData[] packages;
            public BinaryData[] aotMetadata;
            public BinaryData[] hotfixAssemblies;
            public string entryAssembly;
            public string entryType;
            public string entryMethod;
        }

        /// <summary>发行 JSON 中一个包的版本与启动资源。</summary>
        [Serializable]
        private sealed class PackageData
        {
            public string alias;
            public string version;
            public string[] startupAddresses;
        }

        /// <summary>发行 JSON 中一个程序集字节资产。</summary>
        [Serializable]
        private sealed class BinaryData
        {
            public string address;
            public string assemblyName;
            public string sha256;
            public string[] dependencies;
        }

        /// <summary>启动计划读取的包版本。</summary>
        internal readonly struct PackageVersion
        {
            internal string Alias { get; }
            internal string Version { get; }
            internal IReadOnlyList<string> StartupAddresses { get; }

            internal PackageVersion(string alias, string version, IReadOnlyList<string> startupAddresses)
            {
                Alias = alias?.Trim();
                Version = version?.Trim();
                StartupAddresses = startupAddresses;
            }
        }

        /// <summary>启动计划读取的字节资产声明。</summary>
        internal readonly struct Binary
        {
            internal string Address { get; }
            internal string AssemblyName { get; }
            internal string Sha256 { get; }
            internal IReadOnlyList<string> Dependencies { get; }

            internal Binary(string address, string assemblyName, string sha256, IReadOnlyList<string> dependencies)
            {
                Address = address?.Trim();
                AssemblyName = assemblyName?.Trim();
                Sha256 = sha256?.Trim();
                Dependencies = dependencies;
            }
        }

        private readonly Data _data;

        /// <summary>整套发行唯一标识。</summary>
        public string ReleaseId => _data.releaseId?.Trim();
        /// <summary>发行要求的 Player 基线。</summary>
        public string PlayerBaselineId => _data.playerBaselineId?.Trim();
        /// <summary>发行对应的 Player 平台。</summary>
        public RuntimePlatform TargetPlatform => _data.targetPlatform;
        /// <summary>发行对应的 Player 构建类型。</summary>
        public HotUpdateBuildKind BuildKind => _data.buildKind;

        internal string EntryAssembly => _data.entryAssembly?.Trim();
        internal string EntryType => _data.entryType?.Trim();
        internal string EntryMethod => _data.entryMethod?.Trim();
        internal int PackageCount => _data.packages.Length;
        internal int AotMetadataCount => _data.aotMetadata.Length;
        internal int AssemblyCount => _data.hotfixAssemblies.Length;

        private HotUpdateReleaseRecord(Data data) => _data = data;

        internal HotUpdateReleaseRecord(string releaseId, string playerBaselineId, RuntimePlatform targetPlatform,
            HotUpdateBuildKind buildKind, IReadOnlyList<PackageVersion> packages, IReadOnlyList<Binary> aotMetadata,
            IReadOnlyList<Binary> hotfixAssemblies, string entryAssembly, string entryType, string entryMethod)
            : this(new Data
            {
                schemaVersion = SCHEMA_VERSION,
                releaseId = releaseId,
                playerBaselineId = playerBaselineId,
                targetPlatform = targetPlatform,
                buildKind = buildKind,
                entryContractVersion = ENTRY_CONTRACT_VERSION,
                packages = CopyPackages(packages),
                aotMetadata = CopyBinaries(aotMetadata),
                hotfixAssemblies = CopyBinaries(hotfixAssemblies),
                entryAssembly = entryAssembly,
                entryType = entryType,
                entryMethod = entryMethod,
            })
        {
            if (!Enum.IsDefined(typeof(HotUpdateBuildKind), buildKind))
                throw new ArgumentOutOfRangeException(nameof(buildKind));
        }

        #region 公共方法

        /// <summary>读取发行记录并校验 JSON 协议版本及基础字段。</summary>
        /// <param name="json">UTF-8 文件解码后的 JSON 对象。</param>
        /// <returns>不可变的发行记录。</returns>
        public static HotUpdateReleaseRecord Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("发行记录为空。");

            Data data;
            try { data = JsonUtility.FromJson<Data>(json); }
            catch (ArgumentException exception) { throw new FormatException("发行记录不是有效的 JSON。", exception); }

            if (data == null || data.schemaVersion != SCHEMA_VERSION)
                throw new FormatException($"发行记录格式版本无效，当前支持 {SCHEMA_VERSION}。");
            if (data.entryContractVersion != ENTRY_CONTRACT_VERSION)
                throw new FormatException($"发行记录入口契约版本无效，当前支持 {ENTRY_CONTRACT_VERSION}。");
            if (!Enum.IsDefined(typeof(HotUpdateBuildKind), data.buildKind) ||
                !Enum.IsDefined(typeof(RuntimePlatform), data.targetPlatform))
                throw new FormatException("发行记录中的平台或构建类型无效。");
            if (string.IsNullOrWhiteSpace(data.releaseId) || string.IsNullOrWhiteSpace(data.playerBaselineId) ||
                data.packages == null || data.aotMetadata == null || data.hotfixAssemblies == null)
                throw new FormatException("发行记录缺少发行 ID、Player 基线或产物清单。");

            return new HotUpdateReleaseRecord(data);
        }

        /// <summary>按固定协议编码发行记录。</summary>
        /// <param name="isPretty">是否缩进输出。</param>
        /// <returns>用于发布或持久化的 JSON。</returns>
        public string ToJson(bool isPretty = false) => JsonUtility.ToJson(_data, isPretty);

        #endregion

        #region 私有逻辑

        internal PackageVersion GetPackage(int index)
        {
            PackageData package = _data.packages[index];
            return package == null ? default : new PackageVersion(package.alias, package.version,
                Array.AsReadOnly(package.startupAddresses ?? Array.Empty<string>()));
        }

        internal Binary GetAotMetadata(int index) => ReadBinary(_data.aotMetadata[index]);
        internal Binary GetAssembly(int index) => ReadBinary(_data.hotfixAssemblies[index]);

        private static Binary ReadBinary(BinaryData binary) => binary == null ? default : new Binary(
            binary.address, binary.assemblyName, binary.sha256,
            Array.AsReadOnly(binary.dependencies ?? Array.Empty<string>()));

        private static PackageData[] CopyPackages(IReadOnlyList<PackageVersion> packages)
        {
            var result = new PackageData[packages.Count];
            for (int i = 0; i < result.Length; i++)
            {
                PackageVersion package = packages[i];
                result[i] = new PackageData
                {
                    alias = package.Alias,
                    version = package.Version,
                    startupAddresses = CopyStrings(package.StartupAddresses),
                };
            }
            return result;
        }

        private static BinaryData[] CopyBinaries(IReadOnlyList<Binary> binaries)
        {
            var result = new BinaryData[binaries.Count];
            for (int i = 0; i < result.Length; i++)
            {
                Binary binary = binaries[i];
                result[i] = new BinaryData
                {
                    address = binary.Address,
                    assemblyName = binary.AssemblyName,
                    sha256 = binary.Sha256,
                    dependencies = CopyStrings(binary.Dependencies),
                };
            }
            return result;
        }

        private static string[] CopyStrings(IReadOnlyList<string> entries)
        {
            if (entries == null || entries.Count == 0) return Array.Empty<string>();
            var result = new string[entries.Count];
            for (int i = 0; i < result.Length; i++) result[i] = entries[i];
            return result;
        }

        #endregion
    }
}
