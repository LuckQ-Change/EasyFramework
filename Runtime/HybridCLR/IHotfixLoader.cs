using System.Reflection;

namespace EasyFramework
{
    /// <summary>隔离 DLL 和 AOT 元数据的具体加载实现。</summary>
    public interface IHotfixLoader
    {
        void LoadMetadataForAOT(byte[] dllBytes);
        Assembly LoadAssembly(byte[] dllBytes);
    }
}
