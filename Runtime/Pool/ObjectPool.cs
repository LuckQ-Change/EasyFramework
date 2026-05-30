using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class ObjectPool<T> where T : class
    {
        private readonly Stack<T> _stack;
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly int _maxSize;

        public int CountInactive => _stack.Count;
        public int CountAll { get; private set; }

        public ObjectPool(
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            int maxSize = 1024)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (maxSize <= 0) throw new ArgumentException("maxSize must be > 0", nameof(maxSize));

            _factory = factory;
            _onGet = onGet;
            _onRelease = onRelease;
            _maxSize = maxSize;
            _stack = new Stack<T>();
        }

        public T Get()
        {
            T item;
            if (_stack.Count > 0)
            {
                item = _stack.Pop();
            }
            else
            {
                item = _factory();
                CountAll++;
            }

            _onGet?.Invoke(item);
            (item as IPoolable)?.OnSpawn();
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;

            (item as IPoolable)?.OnDespawn();
            _onRelease?.Invoke(item);

            if (_stack.Count >= _maxSize)
            {
                CountAll--;
                return;
            }
            _stack.Push(item);
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var item = _factory();
                CountAll++;
                _stack.Push(item);
            }
        }

        public void Clear()
        {
            _stack.Clear();
            CountAll = 0;
        }
    }
}
