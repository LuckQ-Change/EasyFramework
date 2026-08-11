using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class PoolModule : ModuleSingleton<PoolModule>
    {
        private readonly struct PoolKey : IEquatable<PoolKey>
        {
            public readonly Type Type;
            public readonly string Name;

            public PoolKey(Type type, string name)
            {
                Type = type;
                Name = name ?? string.Empty;
            }

            public bool Equals(PoolKey other) => Type == other.Type && Name == other.Name;
            public override bool Equals(object obj) => obj is PoolKey other && Equals(other);
            public override int GetHashCode() => (Type.GetHashCode() * 397) ^ Name.GetHashCode();
        }

        private readonly Dictionary<PoolKey, IObjectPool> _pools =
            new Dictionary<PoolKey, IObjectPool>();

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

        public T Spawn<T>() where T : class, new()
        {
            return GetOrCreate<T>().Get();
        }

        public T Spawn<T>(string poolName, Func<T> factory) where T : class
        {
            return GetOrCreate(poolName, factory).Get();
        }

        public void Despawn<T>(T item) where T : class
        {
            Despawn(string.Empty, item);
        }

        public void Despawn<T>(string poolName, T item) where T : class
        {
            if (item == null) return;
            var key = new PoolKey(typeof(T), poolName);
            if (!_pools.TryGetValue(key, out var pool))
            {
                throw new InvalidOperationException(
                    $"Pool {typeof(T).Name}/{key.Name} does not exist. Spawn or create it before despawning.");
            }
            ((ObjectPool<T>)pool).Release(item);
        }

        public void Clear<T>() where T : class
        {
            Clear<T>(string.Empty);
        }

        public void Clear<T>(string poolName) where T : class
        {
            if (_pools.TryGetValue(new PoolKey(typeof(T), poolName), out var pool))
            {
                pool.Clear();
            }
        }

        public void ClearAll()
        {
            foreach (var pool in _pools.Values) pool.Clear();
            _pools.Clear();
        }

        protected override void OnShutdown()
        {
            ClearAll();
        }

        private ObjectPool<T> GetOrCreateInternal<T>(
            string poolName,
            Func<T> factory,
            Action<T> onGet,
            Action<T> onRelease,
            int maxSize,
            Action<T> onDestroy,
            bool collectionCheck) where T : class
        {
            var key = new PoolKey(typeof(T), poolName);
            if (_pools.TryGetValue(key, out var existing)) return (ObjectPool<T>)existing;

            var pool = new ObjectPool<T>(
                factory, onGet, onRelease, maxSize, onDestroy, collectionCheck);
            _pools.Add(key, pool);
            return pool;
        }
    }
}
