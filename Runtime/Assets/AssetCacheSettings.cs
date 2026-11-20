using System;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>空闲缓存预算。估计大小只覆盖单个对象，不含共享依赖。</summary>
    [Serializable]
    public sealed class AssetCacheSettings
    {
        [SerializeField, Min(0)] private float _idleSeconds = 15;
        [SerializeField, Min(0)] private int _maxIdleAssets = 32;
        [SerializeField, Min(0)] private int _maxIdleMegabytes = 24;
        [SerializeField, Min(1)] private int _maxConcurrentLoads = 6;
        [SerializeField, Min(1)] private int _maxConcurrentPackageInitializations = 2;

        internal double IdleSeconds => float.IsNaN(_idleSeconds) ? 0 : Math.Max(0, _idleSeconds);
        internal int MaxIdleAssets => Math.Max(0, _maxIdleAssets);
        internal long MaxIdleBytes => Math.Max(0, _maxIdleMegabytes) * 1024L * 1024L;
        internal int MaxConcurrentLoads => Math.Max(1, _maxConcurrentLoads);
        internal int MaxConcurrentPackageInitializations => Math.Max(1, _maxConcurrentPackageInitializations);

        public AssetCacheSettings() { }

        public AssetCacheSettings(float idleSeconds, int maxIdleAssets, int maxIdleMegabytes, int maxConcurrentLoads,
            int maxConcurrentPackageInitializations = 2)
        {
            _idleSeconds = idleSeconds;
            _maxIdleAssets = maxIdleAssets;
            _maxIdleMegabytes = maxIdleMegabytes;
            _maxConcurrentLoads = maxConcurrentLoads;
            _maxConcurrentPackageInitializations = maxConcurrentPackageInitializations;
        }

        internal static AssetCacheSettings ForCurrentPlatform()
        {
#if (UNITY_WEBGL || WEIXINMINIGAME || UNITY_WECHATMINIGAME || UNITY_MINIGAME) && !UNITY_EDITOR
            return new AssetCacheSettings(5, 12, 8, 3);
#else
            return Runtime.IsWeb ? new AssetCacheSettings(5, 12, 8, 3) : new AssetCacheSettings();
#endif
        }
    }
}
