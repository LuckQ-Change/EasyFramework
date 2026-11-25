using UnityEngine;

namespace EasyFramework
{
    /// <summary>为编辑器校验提供 YooAsset 收集器中的资源包名称。</summary>
    public interface IYooAssetPackageConfig
    {
        /// <summary>YooAsset 资源收集配置中的包名。</summary>
        string PackageName { get; }
    }

    /// <summary>可在启动配置中注册的资源包。加载器在首次使用该包时才初始化。</summary>
    public abstract class AssetPackageConfig : ScriptableObject
    {
        [SerializeField] private string _alias = AssetModule.MainPackage;
        public string Alias => _alias;
        public abstract IAssetLoader CreateLoader();
    }
}
