using System;
using System.Reflection;
using HybridCLR;

namespace EasyFramework
{
    /// <summary>通过可选的 HybridCLR SDK 补充元数据并装载热更程序集。</summary>
    public class HybridCLRLoader : IHotfixLoader
    {
        private readonly HomologousImageMode _mode;

        /// <summary>创建指定同源模式的 HybridCLR 加载器。</summary>
        /// <param name="mode">SDK 校验 AOT 元数据使用的同源模式。</param>
        public HybridCLRLoader(HomologousImageMode mode = HomologousImageMode.SuperSet)
        {
            _mode = mode;
        }

        /// <summary>补充单个 AOT 程序集的元数据，SDK 返回错误时抛出异常。</summary>
        /// <param name="dllBytes">与本机 Player 兼容的 AOT 程序集字节。</param>
        public void LoadMetadataForAOT(byte[] dllBytes)
        {
            HybridCLRRuntimeProvider.ValidateTarget(hasAotMetadata: true);
            if (dllBytes == null || dllBytes.Length == 0)
                throw new ArgumentException("HybridCLR AOT 元数据不能为空。", nameof(dllBytes));
            LoadImageErrorCode code = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, _mode);
            if (code != LoadImageErrorCode.OK)
                throw new InvalidOperationException($"HybridCLR 补充 AOT 元数据失败（{code}，错误码 {(int)code}）。");
        }

        /// <summary>装载一个热更程序集。</summary>
        /// <param name="dllBytes">已校验的热更程序集字节。</param>
        /// <returns>成功装载的程序集。</returns>
        public Assembly LoadAssembly(byte[] dllBytes)
        {
            HybridCLRRuntimeProvider.ValidateTarget(hasAotMetadata: false);
            return Assembly.Load(dllBytes);
        }
    }
}
