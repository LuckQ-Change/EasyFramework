using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;

namespace EasyFramework.Editor
{
    /// <summary>按构建目标检查启动配置和各资源适配器的内置离线产物。</summary>
    internal static class FrameworkOfflineBuildValidation
    {
        /// <summary>保存配置中的资源包以及预检需要查询的包内地址。</summary>
        private sealed class PackageCheck
        {
            internal AssetPackageConfig Config { get; }
            internal string Version { get; set; }
            internal SortedSet<string> RequiredAddresses { get; } = new(StringComparer.Ordinal);

            internal PackageCheck(AssetPackageConfig config)
            {
                Config = config;
            }
        }

        internal static string Validate(FrameworkStartupConfig startup, BuildTarget target, bool isServer)
        {
            startup.ValidateStartupMode();
            var packages = new Dictionary<string, PackageCheck>(StringComparer.Ordinal);
            foreach (AssetPackageConfig package in startup.AssetPackages)
            {
                if (package == null) throw new InvalidOperationException("Startup Config 中有空的资源包。");
                string alias = AssetModule.NormalizeAlias(package.Alias);
                if (!packages.TryAdd(alias, new PackageCheck(package)))
                    throw new InvalidOperationException($"资源包别名「{alias}」重复。");
            }

            foreach (string address in startup.ResidentAssets)
                AddRequiredAddress(address, packages);
            if (startup.PageManifest != null)
            {
                foreach (var page in startup.PageManifest.Pages)
                    if (page != null) AddRequiredAddress(page.PrefabAddress, packages);
            }

            var report = new StringBuilder().Append("构建目标 ").Append(target);
            if (isServer) report.Append("（专用服务器）");
            report.AppendLine();
            HotUpdateStartupConfig release = startup.HotUpdate;
            if (release != null)
            {
                var platform = FrameworkHotUpdateSetup.ResolvePlayerPlatform(target, isServer);
                HotUpdatePlan plan = release.BuildPlan(startup, platform ?? release.TargetPlatform);
                foreach (HotUpdatePlan.Package package in plan.Packages)
                {
                    PackageCheck check = packages[package.Alias];
                    check.Version = package.Version;
                    // 启动资源、AOT 元数据和热更程序集已由发行计划统一收集。
                    check.RequiredAddresses.UnionWith(package.RequiredAddresses);
                }
                report.AppendLine($"随包发行 {plan.ReleaseId}");
            }

            bool hasPackageReport = false;
            foreach (var pair in packages.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                PackageCheck package = pair.Value;
                try
                {
                    string result = EditorPackageAddressProviders.ValidateOfflinePackage(package.Config, package.Version,
                        package.RequiredAddresses.ToArray(), target);
                    if (result == null) continue;
                    hasPackageReport = true;
                    report.AppendLine(result);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"「{startup.name}」的资源包「{pair.Key}」离线校验失败。{exception.Message}", exception);
                }
            }

            if (!hasPackageReport) report.AppendLine("没有配置需要进行内置产物校验的资源包。");
            return report.ToString().TrimEnd();
        }

        /// <summary>检查选定启动配置的离线产物，校验失败时中止构建。</summary>
        /// <param name="startups">本次构建的场景依赖所引用的启动配置。</param>
        /// <param name="options">本次 Player 构建的目标和子目标。</param>
        internal static void CheckBuild(IReadOnlyList<FrameworkStartupConfig> startups, BuildPlayerOptions options)
        {
            bool isServer = BuildPipeline.GetBuildTargetGroup(options.target) == BuildTargetGroup.Standalone &&
                            options.subtarget == (int)StandaloneBuildSubtarget.Server;
            var errors = new List<string>();
            foreach (FrameworkStartupConfig startup in startups)
            {
                if (startup.RuntimeMode != RuntimeMode.Offline) continue;
                try { Validate(startup, options.target, isServer); }
                catch (Exception exception)
                {
                    errors.Add($"{AssetDatabase.GetAssetPath(startup)} · {exception.Message}");
                }
            }
            if (errors.Count > 0)
                throw new BuildFailedException("离线 Player 构建校验失败。\n" + string.Join("\n", errors));
        }

        private static void AddRequiredAddress(string address, Dictionary<string, PackageCheck> packages)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            int separator = address.IndexOf(':');
            string alias = separator < 0 ? AssetModule.MainPackage : address.Substring(0, separator).Trim();
            if (!packages.TryGetValue(alias, out PackageCheck package)) return;
            string location = (separator < 0 ? address : address.Substring(separator + 1)).Trim();
            if (location.Length == 0 || location.IndexOf(':') >= 0)
                throw new InvalidOperationException($"资源地址「{address}」格式无效。请使用包内地址或包别名:包内地址。");
            package.RequiredAddresses.Add(location);
        }
    }
}
