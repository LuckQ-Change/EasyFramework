using System;
using UnityEngine;
using YooAsset;

namespace EasyFramework
{
    /// <summary>桥接 YooAsset Mini Game 示例中的微信文件系统。</summary>
    [CreateAssetMenu(fileName = "YooAssetWechatFileSystem", menuName = "EasyFramework/Assets/WeChat File System")]
    public sealed class YooAssetWechatFileSystem : YooAssetWebFileSystem
    {
        public override FileSystemParameters CreateParameters(IRemoteService remote, int downloadConcurrency)
        {
#if UNITY_WEBGL && (WEIXINMINIGAME || UNITY_WECHATMINIGAME) && !UNITY_EDITOR
            FileSystemParameters parameters = WechatFileSystemCreater.CreateFileSystemParameters(remote);
            parameters.AddParameter(EFileSystemParameter.DownloadMaxConcurrency, downloadConcurrency);
            parameters.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame, 1);
            return parameters;
#else
            throw new PlatformNotSupportedException("This file system requires the WeChat SDK WebGL build target.");
#endif
        }
    }
}
