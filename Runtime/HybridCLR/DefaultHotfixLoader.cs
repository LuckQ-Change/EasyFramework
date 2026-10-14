using System.Reflection;

namespace EasyFramework
{
    /// <summary>使用普通程序集加载路径的热更适配器。</summary>
    public class DefaultHotfixLoader : IHotfixLoader
    {
        public void LoadMetadataForAOT(byte[] dllBytes)
        {
        }

        public Assembly LoadAssembly(byte[] dllBytes)
        {
            return Assembly.Load(dllBytes);
        }
    }
}
