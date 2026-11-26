using System;
using System.Reflection;

namespace EasyFramework
{
    /// <summary>使用普通程序集加载路径的热更适配器。</summary>
    public class DefaultHotfixLoader : IHotfixLoader
    {
        /// <summary>普通程序集加载器不提供 AOT 元数据补充能力。</summary>
        /// <param name="dllBytes">请求补充的 AOT 程序集字节。</param>
        public void LoadMetadataForAOT(byte[] dllBytes) =>
            throw new NotSupportedException("普通程序集加载器不支持补充 AOT 元数据。请选择 HybridCLR 运行时提供者。");

        /// <summary>使用托管运行时装载热更程序集。</summary>
        /// <param name="dllBytes">要装载的热更程序集字节。</param>
        /// <returns>成功装载的程序集。</returns>
        public Assembly LoadAssembly(byte[] dllBytes) => Assembly.Load(dllBytes);
    }
}
