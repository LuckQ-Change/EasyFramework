using System.Reflection;

namespace EasyFramework
{
    public interface IHotfixLoader
    {
        void LoadMetadataForAOT(byte[] dllBytes);
        Assembly LoadAssembly(byte[] dllBytes);
    }
}
