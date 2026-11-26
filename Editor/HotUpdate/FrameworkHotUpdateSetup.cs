using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EasyFramework.Editor
{
    /// <summary>为安装向导和启动配置 Inspector 提供共同的热更配置校验。</summary>
    internal static class FrameworkHotUpdateSetup
    {
        /// <summary>校验热更发行、渠道和当前构建平台，并读取脚本后端设置。</summary>
        /// <param name="startup">包含整套发行资产和 Player 基线的启动配置。</param>
        /// <returns>配置校验结果及当前脚本后端说明。</returns>
        internal static string Validate(FrameworkStartupConfig startup)
        {
            if (startup == null) throw new ArgumentNullException(nameof(startup));
            HotUpdateStartupConfig release = startup.HotUpdate;
            if (release == null) throw new InvalidOperationException("请指定随包发行。");
            if (startup.HotUpdateChannel != null) startup.HotUpdateChannel.Validate();

            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(buildTarget);
            bool isServer = group == BuildTargetGroup.Standalone &&
                            EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
            RuntimePlatform? platform = ResolvePlayerPlatform(buildTarget, isServer);
            release.Validate(startup, platform ?? release.TargetPlatform);

            string platformStatus = platform.HasValue
                ? $"Player 平台 {platform.Value} 与发行匹配。"
                : "当前目标的 Player 平台将在实际启动时校验。";
            return $"发行 {release.ReleaseId} · {buildTarget}\n{platformStatus}\n" +
                   $"{DescribeScriptingBackend(group, isServer)}\n包清单和产物摘要将在 Player 启动时校验。";
        }

        internal static RuntimePlatform? ResolvePlayerPlatform(BuildTarget buildTarget, bool isServer)
        {
            switch (buildTarget)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return isServer ? RuntimePlatform.WindowsServer : RuntimePlatform.WindowsPlayer;
                case BuildTarget.StandaloneOSX: return isServer ? RuntimePlatform.OSXServer : RuntimePlatform.OSXPlayer;
                case BuildTarget.StandaloneLinux64: return isServer ? RuntimePlatform.LinuxServer : RuntimePlatform.LinuxPlayer;
                case BuildTarget.iOS: return RuntimePlatform.IPhonePlayer;
                case BuildTarget.WebGL: return RuntimePlatform.WebGLPlayer;
                default:
                    return Enum.TryParse(buildTarget.ToString(), out RuntimePlatform platform) &&
                           Enum.IsDefined(typeof(RuntimePlatform), platform) ? platform : (RuntimePlatform?)null;
            }
        }

        private static string DescribeScriptingBackend(BuildTargetGroup group, bool isServer)
        {
            const string unknownBackend = "请在 Player Settings 核对当前目标的 IL2CPP 脚本后端。";
            if (group == BuildTargetGroup.Unknown) return unknownBackend;
            try
            {
                ScriptingImplementation backend = GetScriptingBackend(group, isServer);
                return backend == ScriptingImplementation.IL2CPP
                    ? "当前目标已配置 IL2CPP 脚本后端。"
                    : $"当前脚本后端为 {backend}，验证 HybridCLR Player 前请在 Player Settings 选择 IL2CPP。";
            }
            catch (ArgumentException)
            {
                return unknownBackend;
            }
        }

        internal static ScriptingImplementation GetScriptingBackend(BuildTargetGroup group, bool isServer)
        {
            NamedBuildTarget target = isServer ? NamedBuildTarget.Server : NamedBuildTarget.FromBuildTargetGroup(group);
            return PlayerSettings.GetScriptingBackend(target);
        }
    }
}
