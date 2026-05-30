using System.Reflection;

namespace EasyFramework
{
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
