#if EASY_HYBRIDCLR
using System.Reflection;
using HybridCLR;

namespace EasyFramework
{
    /// <summary>可选 HybridCLR 适配器；需要目标项目提供对应包与宏。</summary>
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
