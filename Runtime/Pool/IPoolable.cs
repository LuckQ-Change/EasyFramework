namespace EasyFramework
{
    /// <summary>定义对象借出与归还时的回调，不负责创建或销毁对象。</summary>
    public interface IPoolable
    {
        void OnSpawn();
        void OnDespawn();
    }
}
