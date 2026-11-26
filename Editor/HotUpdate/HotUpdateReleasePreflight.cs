using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor
{
    /// <summary>核对 Player 构建基线、收集地址、发行字节和待上传资源包。</summary>
    internal static class HotUpdateReleasePreflight
    {
        internal static (HotUpdateReleaseRecord record, string report) Validate(HotUpdateChannelConfig channel,
            FrameworkStartupConfig startup, HotUpdateStartupConfig release, string receiptPath, string outputDirectory)
        {
            if (channel == null || startup == null || release == null)
                throw new InvalidOperationException("请选择发行渠道、Player 启动配置和要发布的发行。");
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                throw new InvalidOperationException("请选择包含本次资源构建产物的待上传目录。");
            channel.Validate();
            HotUpdatePlayerBuildReceipt receipt = HotUpdatePlayerBuildReceipt.Read(receiptPath);
            ValidateBaseline(startup, receipt);

            HotUpdateReleaseRecord record = release.CreateReleaseRecord(receipt.buildKind);
            HotUpdatePlan plan = release.BuildPlan(startup, receipt.platform, receipt.buildKind, record);
            var collected = new EditorPackageAddresses();
            var pathsByAlias = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            var binaryDigestsByAlias = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            var report = new StringBuilder();
            report.AppendLine($"发行 {plan.ReleaseId} · Player {receipt.platform} · {receipt.buildKind}")
                .AppendLine($"基线 {record.PlayerBaselineId} · 构建 {receipt.buildGuid}")
                .AppendLine($"Unity {receipt.unityVersion} · {receipt.buildTarget} · {receipt.backend}");

            foreach (HotUpdatePlan.Package package in plan.Packages)
            {
                try
                {
                    if (package.Backend is not AssetPackageConfig config)
                        throw new InvalidOperationException("后端缺少可定位的资源包配置资产。");
                    IReadOnlyDictionary<string, string> paths = collected.Collect(config);
                    foreach (string address in package.RequiredAddresses)
                        if (!paths.TryGetValue(address, out string path) || string.IsNullOrEmpty(path))
                            throw new InvalidOperationException($"未收集启动地址「{address}」。");
                    pathsByAlias.Add(package.Alias, paths);
                    binaryDigestsByAlias.Add(package.Alias, new Dictionary<string, string>(StringComparer.Ordinal));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"包「{package.Alias}」版本「{package.Version}」校验失败。{exception.Message}", exception);
                }
                report.AppendLine($"包 {package.Alias} · 版本 {package.Version} · 已核对 {package.RequiredAddresses.Count} 个启动地址");
            }

            ValidateSourceBinaries(plan.AotMetadata, pathsByAlias, binaryDigestsByAlias, "AOT 元数据");
            ValidateSourceBinaries(plan.Assemblies, pathsByAlias, binaryDigestsByAlias, "热更 DLL");
            report.AppendLine($"已核对工程源文件中 {plan.AotMetadata.Count} 个 AOT 元数据和 {plan.Assemblies.Count} 个热更 DLL 的程序集名称与 SHA-256。");
            foreach (HotUpdatePlan.Package package in plan.Packages)
            {
                try
                {
                    string result = EditorPackageAddressProviders.ValidateBuildOutput((AssetPackageConfig)package.Backend,
                        package.Version, outputDirectory, package.RequiredAddresses, pathsByAlias[package.Alias],
                        binaryDigestsByAlias[package.Alias]);
                    report.AppendLine(result);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"包「{package.Alias}」版本「{package.Version}」的待上传产物校验失败。{exception.Message}", exception);
                }
            }
            return (record, report.ToString());
        }

        private static void ValidateBaseline(FrameworkStartupConfig startup, HotUpdatePlayerBuildReceipt receipt)
        {
            if (receipt.backend != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException($"构建 {receipt.buildGuid} 使用 {receipt.backend}。热更发行要求 IL2CPP Player。");
            string path = AssetDatabase.GetAssetPath(startup);
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException("请选择已保存的 Player 启动配置。");
            HotUpdatePlayerBuildReceipt.Startup built = Array.Find(receipt.startups,
                entry => entry != null && string.Equals(entry.assetGuid, guid, StringComparison.OrdinalIgnoreCase));
            if (built == null)
                throw new InvalidOperationException($"构建 {receipt.buildGuid} 未包含启动配置「{path}」。");
            if (string.IsNullOrWhiteSpace(built.playerBaselineId) ||
                !string.Equals(built.playerBaselineId, startup.PlayerBaselineId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Player 基线不匹配。构建记录为「{built.playerBaselineId}」，启动配置为「{startup.PlayerBaselineId}」。");
        }

        private static void ValidateSourceBinaries(IReadOnlyList<HotUpdatePlan.Binary> binaries,
            Dictionary<string, IReadOnlyDictionary<string, string>> pathsByAlias,
            Dictionary<string, Dictionary<string, string>> binaryDigestsByAlias, string category)
        {
            foreach (HotUpdatePlan.Binary binary in binaries)
            {
                string path = pathsByAlias[binary.Alias][binary.Location];
                try
                {
                    if (!path.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase) ||
                        AssetDatabase.LoadMainAssetAtPath(path) is not TextAsset || !File.Exists(path))
                        throw new InvalidDataException("收集地址需要指向可读取的 .dll.bytes 主资产。");
                    using var stream = File.OpenRead(path);
                    if (stream.Length == 0) throw new InvalidDataException("文件为空。");
                    using SHA256 sha = SHA256.Create();
                    string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                    if (!string.Equals(digest, binary.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"SHA-256 不匹配。期望 {binary.Sha256}，实际 {digest.ToLowerInvariant()}。");
                    string assemblyName = AssemblyName.GetAssemblyName(path).Name;
                    if (!string.Equals(assemblyName, binary.AssemblyName, StringComparison.Ordinal))
                        throw new InvalidDataException($"程序集名称不匹配。期望「{binary.AssemblyName}」，实际「{assemblyName}」。");

                    binaryDigestsByAlias[binary.Alias].Add(binary.Location, binary.Sha256);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"{category}「{binary.Key}」 · {path} 校验失败。{exception.Message}", exception);
                }
            }
        }
    }
}
