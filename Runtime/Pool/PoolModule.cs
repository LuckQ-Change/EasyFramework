using System;
using System.Collections.Generic;

namespace EasyFramework
{
    /// <summary>管理当前宿主中的默认池与命名池，并关闭它们各自持有的对象。</summary>
    public class PoolModule : Module<PoolModule>
    {
        /// <summary>按对象类型和名称标识同一宿主中的一个池。</summary>
        private readonly struct PoolKey : IEquatable<PoolKey>
        {
            public readonly Type Type;
            public readonly string Name;

            internal PoolKey(Type type, string name)
            {
                Type = type;
                Name = name ?? string.Empty;
            }

            /// <summary>比较两个池键的类型与名称。</summary>
            /// <param name="other">要比较的池键。</param>
            /// <returns>类型和名称都相同时为 true。</returns>
            public bool Equals(PoolKey other) => Type == other.Type && Name == other.Name;
            /// <summary>比较指定实例是否代表相同的池。</summary>
            /// <param name="obj">要比较的实例。</param>
            /// <returns>实例是相同的池键时为 true。</returns>
            public override bool Equals(object obj) => obj is PoolKey other && Equals(other);
            /// <summary>生成由类型和名称共同决定的哈希值。</summary>
            /// <returns>当前池键的哈希值。</returns>
            public override int GetHashCode() => (Type.GetHashCode() * 397) ^ Name.GetHashCode();
        }

        private readonly Dictionary<PoolKey, IObjectPool> _pools =
            new Dictionary<PoolKey, IObjectPool>();
        private bool _isClosed;

        #region 公共方法

        /// <summary>获取指定类型的默认对象池，首次调用时创建。</summary>
        /// <typeparam name="T">池管理的对象类型。</typeparam>
        /// <param name="factory">创建对象的工厂，留空时使用默认构造函数。</param>
        /// <param name="onGet">借出前的初始化回调。</param>
        /// <param name="onRelease">归还时的重置回调。</param>
        /// <param name="maxSize">可缓存的最大空闲对象数。</param>
        /// <param name="onDestroy">对象离开池所有权时的清理回调。</param>
        /// <param name="collectionCheck">开放期间是否对非法归还抛出异常。</param>
        /// <returns>当前宿主为该类型登记的默认池。</returns>
        public ObjectPool<T> GetOrCreate<T>(
            Func<T> factory = null,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int maxSize = 1024,
            Action<T> onDestroy = null,
            bool collectionCheck = true) where T : class, new()
        {
            return GetOrCreateInternal(
                string.Empty,
                factory ?? (() => new T()),
                onGet,
                onRelease,
                maxSize,
                onDestroy,
                collectionCheck);
        }

        /// <summary>获取指定类型的命名对象池，首次调用时创建。</summary>
        /// <typeparam name="T">池管理的对象类型。</typeparam>
        /// <param name="poolName">当前宿主中的池名称。</param>
        /// <param name="factory">创建对象的工厂。</param>
        /// <param name="onGet">借出前的初始化回调。</param>
        /// <param name="onRelease">归还时的重置回调。</param>
        /// <param name="maxSize">可缓存的最大空闲对象数。</param>
        /// <param name="onDestroy">对象离开池所有权时的清理回调。</param>
        /// <param name="collectionCheck">开放期间是否对非法归还抛出异常。</param>
        /// <returns>当前宿主为该类型和名称登记的池。</returns>
        public ObjectPool<T> GetOrCreate<T>(
            string poolName,
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int maxSize = 1024,
            Action<T> onDestroy = null,
            bool collectionCheck = true) where T : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return GetOrCreateInternal(
                poolName,
                factory,
                onGet,
                onRelease,
                maxSize,
                onDestroy,
                collectionCheck);
        }

        /// <summary>从当前宿主的默认池借出一个对象。</summary>
        /// <typeparam name="T">要借出的对象类型。</typeparam>
        /// <returns>池中借出的对象。</returns>
        public T Spawn<T>() where T : class, new()
        {
            return GetOrCreate<T>().Get();
        }

        /// <summary>从当前宿主的指定命名池借出一个对象。</summary>
        /// <typeparam name="T">要借出的对象类型。</typeparam>
        /// <param name="poolName">要使用的池名称。</param>
        /// <param name="factory">首次创建命名池时使用的对象工厂。</param>
        /// <returns>池中借出的对象。</returns>
        public T Spawn<T>(string poolName, Func<T> factory) where T : class
        {
            return GetOrCreate(poolName, factory).Get();
        }

        /// <summary>将对象归还给当前宿主的默认池。</summary>
        /// <typeparam name="T">借出时使用的对象类型。</typeparam>
        /// <param name="item">要归还的对象。</param>
        public void Despawn<T>(T item) where T : class
        {
            Despawn(string.Empty, item);
        }

        /// <summary>将对象归还给原命名池。宿主关闭后的晚归还直接返回。</summary>
        /// <typeparam name="T">借出时使用的对象类型。</typeparam>
        /// <param name="poolName">原池的名称。</param>
        /// <param name="item">要归还的对象。</param>
        public void Despawn<T>(string poolName, T item) where T : class
        {
            if (item == null || _isClosed) return;
            var key = new PoolKey(typeof(T), poolName);
            if (!_pools.TryGetValue(key, out var pool))
            {
                throw new InvalidOperationException(
                    $"Pool {typeof(T).Name}/{key.Name} does not exist. Spawn or create it before despawning.");
            }
            ((ObjectPool<T>)pool).Release(item);
        }

        /// <summary>销毁指定类型默认池中当前的空闲对象。</summary>
        /// <typeparam name="T">池管理的对象类型。</typeparam>
        public void Clear<T>() where T : class
        {
            Clear<T>(string.Empty);
        }

        /// <summary>销毁指定命名池中当前的空闲对象。</summary>
        /// <typeparam name="T">池管理的对象类型。</typeparam>
        /// <param name="poolName">要清理的池名称。</param>
        public void Clear<T>(string poolName) where T : class
        {
            if (_isClosed) return;
            if (_pools.TryGetValue(new PoolKey(typeof(T), poolName), out var pool))
            {
                pool.Clear();
            }
        }

        /// <summary>清理现有池的空闲对象，保留池登记和借出关系。</summary>
        public void ClearAll()
        {
            if (_isClosed || _pools.Count == 0) return;
            // 清理回调可以创建其他命名池，本次只处理调用时已经登记的池。
            var pools = new List<IObjectPool>(_pools.Values);
            foreach (IObjectPool pool in pools) pool.Clear();
        }

        #endregion

        #region 生命周期

        protected override void OnShutdown()
        {
            if (_isClosed) return;
            _isClosed = true;
            foreach (IObjectPool pool in _pools.Values) pool.Close();
            _pools.Clear();
        }

        #endregion

        #region 私有逻辑

        private ObjectPool<T> GetOrCreateInternal<T>(
            string poolName,
            Func<T> factory,
            Action<T> onGet,
            Action<T> onRelease,
            int maxSize,
            Action<T> onDestroy,
            bool collectionCheck) where T : class
        {
            if (_isClosed) throw new ObjectDisposedException(nameof(PoolModule));
            var key = new PoolKey(typeof(T), poolName);
            if (_pools.TryGetValue(key, out var existing)) return (ObjectPool<T>)existing;

            var pool = new ObjectPool<T>(
                factory, onGet, onRelease, maxSize, onDestroy, collectionCheck);
            _pools.Add(key, pool);
            return pool;
        }

        #endregion
    }
}
