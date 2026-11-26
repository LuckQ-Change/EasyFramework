using System;
using HybridCLR;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>通过 HybridCLR SDK 为组合启动链装配运行时加载器。</summary>
    [CreateAssetMenu(fileName = "HybridCLRRuntime", menuName = "EasyFramework/Hot Update/HybridCLR Runtime")]
    public sealed class HybridCLRRuntimeProvider : HotUpdateRuntimeProvider
    {
        [SerializeField] private HomologousImageMode _imageMode = HomologousImageMode.SuperSet;

        /// <summary>检查 HybridCLR 配置资产中的同源模式。</summary>
        /// <param name="hasAotMetadata">本次发行是否要求补充 AOT 元数据。</param>
        public override void ValidateConfiguration(bool hasAotMetadata)
        {
            if (!Enum.IsDefined(typeof(HomologousImageMode), _imageMode))
                throw new InvalidOperationException($"HybridCLR 同源模式无效（{_imageMode}）。");
        }

        /// <summary>检查当前运行环境能否实际执行 HybridCLR 启动。</summary>
        /// <param name="hasAotMetadata">本次启动是否需要补充真实 AOT 元数据。</param>
        public override void ValidateRuntime(bool hasAotMetadata) => ValidateTarget(hasAotMetadata);

        /// <summary>创建使用已配置同源模式的 HybridCLR 加载器。</summary>
        /// <returns>本次启动尝试使用的加载器。</returns>
        public override IHotfixLoader CreateLoader() => new HybridCLRLoader(_imageMode);

        internal static void ValidateTarget(bool hasAotMetadata)
        {
#if UNITY_EDITOR
            if (hasAotMetadata)
                throw new PlatformNotSupportedException("编辑器无法实际补充 HybridCLR AOT 元数据。请使用配置了 HybridCLR 的 IL2CPP Player 验证此发行。");
#elif !ENABLE_IL2CPP
            throw new PlatformNotSupportedException("HybridCLR 启动链需要使用 IL2CPP 构建的 Player。");
#endif
        }
    }
}
