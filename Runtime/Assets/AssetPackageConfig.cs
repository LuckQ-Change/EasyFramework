using UnityEngine;

namespace EasyFramework
{
    /// <summary>可在启动配置中注册的资源包。加载器在首次使用该包时才初始化。</summary>
    public abstract class AssetPackageConfig : ScriptableObject
    {
        [SerializeField] private string _alias = AssetModule.MainPackage;
        public string Alias => _alias;
        public abstract IAssetLoader CreateLoader();
    }
}
