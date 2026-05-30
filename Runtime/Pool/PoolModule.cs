using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class PoolModule : ModuleSingleton<PoolModule>
    {
        private readonly Dictionary<Type, object> _pools = new Dictionary<Type, object>();

        public ObjectPool<T> GetOrCreate<T>(
            Func<T> factory = null,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int maxSize = 1024) where T : class, new()
        {
            var type = typeof(T);
            if (_pools.TryGetValue(type, out var exist))
            {
                return (ObjectPool<T>)exist;
            }

            var pool = new ObjectPool<T>(factory ?? (() => new T()), onGet, onRelease, maxSize);
            _pools.Add(type, pool);
            return pool;
        }

        public T Spawn<T>() where T : class, new()
        {
            return GetOrCreate<T>().Get();
        }

        public void Despawn<T>(T item) where T : class, new()
        {
            if (item == null) return;
            GetOrCreate<T>().Release(item);
        }

        public void Clear<T>() where T : class
        {
            if (_pools.TryGetValue(typeof(T), out var pool))
            {
                ((ObjectPool<T>)pool).Clear();
            }
        }

        public void ClearAll()
        {
            foreach (var kv in _pools)
            {
                var clearMethod = kv.Value.GetType().GetMethod("Clear");
                clearMethod?.Invoke(kv.Value, null);
            }
            _pools.Clear();
        }

        protected override void OnShutdown()
        {
            ClearAll();
        }
    }
}
