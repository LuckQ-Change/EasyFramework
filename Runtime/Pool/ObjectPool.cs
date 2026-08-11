using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EasyFramework
{
    internal interface IObjectPool
    {
        void Clear();
    }

    public class ObjectPool<T> : IObjectPool where T : class
    {
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Stack<T> _stack;
        private readonly HashSet<T> _inactive;
        private readonly HashSet<T> _owned;
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxSize;
        private readonly bool _collectionCheck;

        public int CountInactive => _stack.Count;
        public int CountAll { get; private set; }
        public int CountActive => CountAll - CountInactive;

        public ObjectPool(
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int maxSize = 1024,
            Action<T> onDestroy = null,
            bool collectionCheck = true)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (maxSize <= 0) throw new ArgumentException("maxSize must be > 0", nameof(maxSize));

            _factory = factory;
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxSize = maxSize;
            _collectionCheck = collectionCheck;
            _stack = new Stack<T>();
            if (collectionCheck)
            {
                _inactive = new HashSet<T>(ReferenceComparer.Instance);
                _owned = new HashSet<T>(ReferenceComparer.Instance);
            }
        }

        public T Get()
        {
            T item;
            if (_stack.Count > 0)
            {
                item = _stack.Pop();
                _inactive?.Remove(item);
            }
            else
            {
                item = CreateItem();
            }

            _onGet?.Invoke(item);
            (item as IPoolable)?.OnSpawn();
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            if (_collectionCheck)
            {
                if (!_owned.Contains(item))
                {
                    throw new InvalidOperationException("The released object does not belong to this pool.");
                }
                if (_inactive.Contains(item))
                {
                    throw new InvalidOperationException("The object has already been released to this pool.");
                }
            }

            (item as IPoolable)?.OnDespawn();
            _onRelease?.Invoke(item);

            if (_stack.Count >= _maxSize)
            {
                DestroyItem(item);
                return;
            }

            _stack.Push(item);
            _inactive?.Add(item);
        }

        public void Prewarm(int count)
        {
            if (count <= 0) return;
            int createCount = Math.Min(count, _maxSize - _stack.Count);
            for (int i = 0; i < createCount; i++)
            {
                var item = CreateItem();
                try
                {
                    (item as IPoolable)?.OnDespawn();
                    _onRelease?.Invoke(item);
                    _stack.Push(item);
                    _inactive?.Add(item);
                }
                catch
                {
                    DestroyItem(item);
                    throw;
                }
            }
        }

        public void Clear()
        {
            while (_stack.Count > 0)
            {
                DestroyItem(_stack.Pop());
            }
            _inactive?.Clear();
        }

        private T CreateItem()
        {
            var item = _factory();
            if (item == null) throw new InvalidOperationException("Pool factory returned null.");
            CountAll++;
            _owned?.Add(item);
            return item;
        }

        private void DestroyItem(T item)
        {
            _inactive?.Remove(item);
            _owned?.Remove(item);
            CountAll--;
            try
            {
                _onDestroy?.Invoke(item);
            }
            catch (Exception ex)
            {
                Log.Error($"[Pool] destroy callback failed for {typeof(T).Name}: {ex}");
            }
        }
    }
}
