using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EasyFramework.Editor
{
    /// <summary>记录一次实际 Player 构建的热更基线、构建目标和随包配置。</summary>
    [Serializable]
    internal sealed class HotUpdatePlayerBuildReceipt
    {
        internal const int SCHEMA_VERSION = 1;
        private const int MAX_RECEIPT_BYTES = 1024 * 1024;

        /// <summary>构建中实际打包的一份热更启动配置。</summary>
        [Serializable]
        internal sealed class Startup
        {
            public string assetGuid;
            public string playerBaselineId;
        }

        public int schemaVersion;
        public string buildGuid;
        public string unityVersion;
        public string buildTarget;
        public RuntimePlatform platform;
        public ScriptingImplementation backend;
        public HotUpdateBuildKind buildKind;
        public Startup[] startups;

        internal static HotUpdatePlayerBuildReceipt Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("请选择 Player 构建生成的基线记录。");
            if (new FileInfo(path).Length > MAX_RECEIPT_BYTES)
                throw new InvalidDataException("Player 基线记录超过大小限制。");

            HotUpdatePlayerBuildReceipt receipt = JsonUtility.FromJson<HotUpdatePlayerBuildReceipt>(File.ReadAllText(path));
            if (receipt == null || receipt.schemaVersion != SCHEMA_VERSION ||
                !Guid.TryParse(receipt.buildGuid, out Guid buildGuid) || buildGuid == Guid.Empty ||
                string.IsNullOrWhiteSpace(receipt.buildTarget) || string.IsNullOrWhiteSpace(receipt.unityVersion) ||
                !Enum.IsDefined(typeof(RuntimePlatform), receipt.platform) ||
                (receipt.buildKind != HotUpdateBuildKind.Release && receipt.buildKind != HotUpdateBuildKind.Development) ||
                receipt.startups == null || receipt.startups.Length == 0)
                throw new InvalidDataException("Player 基线记录不完整或格式不受支持。");
            return receipt;
        }
    }

    /// <summary>从成功构建的打包资产中提取热更基线，写到 Player 输出路径旁。</summary>
    internal sealed class HotUpdatePlayerBuildReceiptWriter : IPostprocessBuildWithReport
    {
        /// <summary>在 Player 构建的其他常规收尾步骤后记录基线。</summary>
        public int callbackOrder => int.MaxValue;

        /// <summary>记录构建中实际包含的热更启动配置。</summary>
        /// <param name="report">本次 Player 构建的报告。</param>
        public void OnPostprocessBuild(BuildReport report)
        {
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Failed || summary.result == BuildResult.Cancelled ||
                summary.totalErrors > 0 || (summary.options & BuildOptions.BuildScriptsOnly) != 0)
                return;

            try
            {
                List<HotUpdatePlayerBuildReceipt.Startup> startups = FindPackedStartups(report);
                if (startups.Count == 0) return;
                BuildTargetGroup group = summary.platformGroup;
                bool isServer = group == BuildTargetGroup.Standalone &&
                                summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server;
                RuntimePlatform? platform = FrameworkHotUpdateSetup.ResolvePlayerPlatform(summary.platform, isServer);
                if (!platform.HasValue)
                    throw new InvalidOperationException($"暂无法映射构建目标 {summary.platform}。");
                string buildGuid = summary.guid.ToString();
                if (!Guid.TryParse(buildGuid, out Guid value) || value == Guid.Empty)
                    throw new InvalidOperationException("当前构建没有有效的 GUID。");
                var receipt = new HotUpdatePlayerBuildReceipt
                {
                    schemaVersion = HotUpdatePlayerBuildReceipt.SCHEMA_VERSION,
                    buildGuid = buildGuid,
                    unityVersion = Application.unityVersion,
                    buildTarget = summary.platform.ToString(),
                    platform = platform.Value,
                    backend = FrameworkHotUpdateSetup.GetScriptingBackend(group, isServer),
                    buildKind = (summary.options & BuildOptions.Development) != 0
                        ? HotUpdateBuildKind.Development : HotUpdateBuildKind.Release,
                    startups = startups.ToArray(),
                };
                string output = Path.GetFullPath(summary.outputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string path = $"{output}.{buildGuid}.easyframework-build.json";
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(file, new UTF8Encoding(false));
                writer.Write(JsonUtility.ToJson(receipt, prettyPrint: true));
                writer.Flush();
                Debug.Log($"[EasyFramework] 已记录热更 Player 基线：{path}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[EasyFramework] 未生成热更 Player 基线记录。{exception.Message}");
            }
        }

        private static List<HotUpdatePlayerBuildReceipt.Startup> FindPackedStartups(BuildReport report)
        {
            var result = new List<HotUpdatePlayerBuildReceipt.Startup>();
            var guids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PackedAssets packed in report.packedAssets ?? Array.Empty<PackedAssets>())
            {
                foreach (PackedAssetInfo asset in packed.contents ?? Array.Empty<PackedAssetInfo>())
                {
                    string guid = asset.sourceAssetGUID.ToString();
                    if (!guids.Add(guid)) continue;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                    FrameworkStartupConfig startup = AssetDatabase.LoadAssetAtPath<FrameworkStartupConfig>(path);
                    if (startup == null || (startup.HotUpdate == null && startup.HotUpdateChannel == null)) continue;
                    result.Add(new HotUpdatePlayerBuildReceipt.Startup
                    {
                        assetGuid = guid,
                        playerBaselineId = startup.PlayerBaselineId,
                    });
                }
            }
            result.Sort((left, right) => string.CompareOrdinal(left.assetGuid, right.assetGuid));
            return result;
        }
    }
}
