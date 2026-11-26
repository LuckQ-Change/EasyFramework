using System.Reflection;

namespace EasyFramework
{
    /// <summary>隔离 DLL 和 AOT 元数据的具体加载实现。</summary>
    public interface IHotfixLoader
    {
        /// <summary>补充单个 AOT 程序集的元数据。失败时抛出包含后端结果的异常。</summary>
        /// <param name="dllBytes">与目标 Player 匹配的 AOT 程序集字节。</param>
        void LoadMetadataForAOT(byte[] dllBytes);

        /// <summary>将已校验的热更程序集装载到当前进程。失败时抛出异常。</summary>
        /// <param name="dllBytes">要装载的热更程序集字节。</param>
        /// <returns>成功装载的程序集。</returns>
        Assembly LoadAssembly(byte[] dllBytes);
    }
}
