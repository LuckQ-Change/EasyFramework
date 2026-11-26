using System;
using System.Linq;
using EasyFramework.Editor.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.SceneManagement;

namespace EasyFramework.Editor
{
    /// <summary>在 Player 构建前确定有效启动配置，统一执行页面清单与离线产物校验。</summary>
    internal sealed class FrameworkPlayerBuildValidation : BuildPlayerProcessor
    {
        /// <summary>按本次构建的场景与目标运行框架校验。</summary>
        /// <param name="context">包含 Player 构建场景与目标的上下文。</param>
        public override void PrepareForBuild(BuildPlayerContext context)
        {
            BuildPlayerOptions options = context.BuildPlayerOptions;
            string[] scenes = options.scenes;
            // 场景列表为空时，Unity 使用当前场景构建。
            if (scenes == null || scenes.Length == 0) scenes = new[] { SceneManager.GetActiveScene().path };

            FrameworkStartupConfig[] startups = FindStartupConfigs(scenes);
            if (startups.Length == 0) return;

            FrameworkOfflineBuildValidation.CheckBuild(startups, options);
            UIValidator.CheckBuild(startups);
        }

        /// <summary>读取指定场景的资产依赖并按路径排序启动配置。</summary>
        /// <param name="scenes">场景资产路径。</param>
        /// <returns>此场景集合引用的启动配置。</returns>
        internal static FrameworkStartupConfig[] FindStartupConfigs(string[] scenes)
        {
            string[] paths = scenes.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
            if (paths.Length == 0) return Array.Empty<FrameworkStartupConfig>();

            return AssetDatabase.GetDependencies(paths, true)
                .Where(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<FrameworkStartupConfig>)
                .Where(startup => startup != null).ToArray();
        }
    }
}
