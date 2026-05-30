#if EASY_HYBRIDCLR
using System.Reflection;
using HybridCLR;

namespace EasyFramework
{
    public class HybridCLRLoader : IHotfixLoader
    {
        private readonly HomologousImageMode _mode;

        public HybridCLRLoader(HomologousImageMode mode = HomologousImageMode.SuperSet)
        {
            _mode = mode;
        }

        public void LoadMetadataForAOT(byte[] dllBytes)
        {
            var err = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, _mode);
            if (err != LoadImageErrorCode.OK)
            {
                Log.Error($"[HybridCLR] LoadMetadataForAOTAssembly failed: {err}");
            }
        }

        public Assembly LoadAssembly(byte[] dllBytes)
        {
            return Assembly.Load(dllBytes);
        }
    }
}
#endif
