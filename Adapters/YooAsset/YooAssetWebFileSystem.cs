using UnityEngine;
using YooAsset;

namespace EasyFramework
{
    /// <summary>接入小游戏 SDK 提供的 YooAsset 文件系统和平台缓存。</summary>
    public abstract class YooAssetWebFileSystem : ScriptableObject
    {
        public abstract FileSystemParameters CreateParameters(IRemoteService remote, int downloadConcurrency);
    }
}
