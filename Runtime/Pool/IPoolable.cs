namespace EasyFramework
{
    /// <summary>提供借出初始化与归还重置的回调。</summary>
    public interface IPoolable
    {
        /// <summary>在对象借给调用方前初始化本次使用状态。</summary>
        void OnSpawn();

        /// <summary>在归还、预热和关闭仍借出的对象时重置使用状态。</summary>
        void OnDespawn();
    }
}
