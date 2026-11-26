using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EasyFramework
{
    /// <summary>供宿主统一清理空闲对象或终止对象池。</summary>
    internal interface IObjectPool
    {
        /// <summary>销毁当前空闲对象。</summary>
        void Clear();

        /// <summary>关闭对象池并清理池持有的对象。</summary>
        void Close();
    }

    /// <summary>管理同一类型对象的借出、复用与终止清理。</summary>
    public class ObjectPool<T> : IObjectPool where T : class
    {
        /// <summary>区分空闲、借出和正在执行用户回调的对象。</summary>
        private enum ObjectState
        {
            Idle,
            Borrowed,
            InCallback,
        }

        /// <summary>以引用身份辨识池持有的对象。</summary>
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<T>.Equals(T x, T y) => ReferenceEquals(x, y);
            int IEqualityComparer<T>.GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Stack<T> _idle = new Stack<T>();
        private readonly Dictionary<T, ObjectState> _owned = new Dictionary<T, ObjectState>(ReferenceComparer.Instance);
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxSize;
        private readonly bool _hasCollectionCheck;
        private bool _isClosed;

        /// <summary>可供再次借出的空闲对象数。</summary>
        public int CountInactive => _idle.Count;
        /// <summary>仍由池持有的对象数。</summary>
        public int CountAll => _owned.Count;
        /// <summary>已借出或正在执行借还回调的对象数。</summary>
        public int CountActive => CountAll - CountInactive;
        /// <summary>池是否已永久关闭。</summary>
        public bool IsClosed => _isClosed;

        #region 公共方法

        /// <summary>为当前调用线程创建一个对象池。</summary>
        /// <param name="factory">创建新对象的工厂。</param>
        /// <param name="onGet">借出前的初始化回调。</param>
        /// <param name="onRelease">对象归还时的重置回调。</param>
        /// <param name="maxSize">可缓存的最大空闲对象数。</param>
        /// <param name="onDestroy">对象离开池所有权时的清理回调。</param>
        /// <param name="collectionCheck">开放期间是否对非法归还抛出异常。</param>
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
            _hasCollectionCheck = collectionCheck;
        }

        /// <summary>借出一个经过初始化的对象。</summary>
        /// <returns>由当前池持有的借出实例。</returns>
        public T Get()
        {
            ThrowIfClosed();
            T item = _idle.Count > 0 ? _idle.Pop() : CreateItem();
            _owned[item] = ObjectState.InCallback;
            try
            {
                _onGet?.Invoke(item);
                ThrowIfClosed();
                (item as IPoolable)?.OnSpawn();
                if (_isClosed) DestroyBorrowedItem(item);
                ThrowIfClosed();
                _owned[item] = ObjectState.Borrowed;
                return item;
            }
            catch
            {
                // 回调失败的对象状态未知；移出池所有权，避免下次 Get 复用半初始化对象。
                DestroyItem(item);
                throw;
            }
        }

        /// <summary>归还当前池借出的对象。已关闭的池忽略晚归还。</summary>
        /// <param name="item">要归还的借出实例。</param>
        public void Release(T item)
        {
            if (item == null || _isClosed) return;
            if (!_owned.TryGetValue(item, out ObjectState state) || state != ObjectState.Borrowed)
            {
                if (_hasCollectionCheck)
                    throw new InvalidOperationException("只能归还本池当前借出的对象。");
                return;
            }

            _owned[item] = ObjectState.InCallback;
            Recycle(item);
        }

        /// <summary>提前创建并重置一批空闲对象。</summary>
        /// <param name="count">本次最多预建的对象数。</param>
        public void Prewarm(int count)
        {
            ThrowIfClosed();
            if (count <= 0) return;
            int createCount = Math.Min(count, _maxSize - _idle.Count);
            for (int i = 0; i < createCount && !_isClosed && _idle.Count < _maxSize; i++)
                Recycle(CreateItem());
        }

        /// <summary>销毁当前空闲对象，保留借出对象与池的可用状态。</summary>
        public void Clear()
        {
            if (!_isClosed) DestroyIdleItems();
        }

        /// <summary>永久关闭池。清理空闲和借出对象，执行中的回调由原操作收尾。</summary>
        public void Close()
        {
            if (_isClosed) return;
            _isClosed = true;
            DestroyIdleItems();
            if (_owned.Count == 0) return;

            // 执行中的借还回调仍持有其对象，以免关闭在回调内部销毁调用者正在使用的对象。
            var borrowed = new List<T>();
            foreach (var entry in _owned)
                if (entry.Value == ObjectState.Borrowed) borrowed.Add(entry.Key);
            foreach (T item in borrowed) DestroyBorrowedItem(item);
        }

        #endregion

        #region 私有逻辑

        private T CreateItem()
        {
            T item = _factory();
            if (item == null) throw new InvalidOperationException("Pool factory returned null.");
            if (!_owned.TryAdd(item, ObjectState.InCallback))
                throw new InvalidOperationException("Pool factory returned an object already owned by this pool.");

            // 工厂可能间接关闭所属宿主，工厂返回的对象也必须在关闭后销毁。
            if (_isClosed)
            {
                DestroyItem(item);
                ThrowIfClosed();
            }
            return item;
        }

        private void Recycle(T item)
        {
            try { NotifyReturn(item); }
            catch
            {
                DestroyItem(item);
                throw;
            }

            if (_isClosed || _idle.Count >= _maxSize)
            {
                DestroyItem(item);
                return;
            }

            _owned[item] = ObjectState.Idle;
            _idle.Push(item);
        }

        private void NotifyReturn(T item)
        {
            (item as IPoolable)?.OnDespawn();
            _onRelease?.Invoke(item);
        }

        private void DestroyIdleItems()
        {
            if (_idle.Count == 0) return;
            // 在调用销毁回调前移出整个空闲批次，回调中重新清理也不会销毁同一批对象。
            T[] items = _idle.ToArray();
            _idle.Clear();
            foreach (T item in items) _owned.Remove(item);
            foreach (T item in items) InvokeDestroy(item);
        }

        private void DestroyItem(T item)
        {
            if (_owned.Remove(item)) InvokeDestroy(item);
        }

        private void DestroyBorrowedItem(T item)
        {
            if (!_owned.Remove(item)) return;
            try { NotifyReturn(item); }
            catch (Exception exception) { Log.Error($"[Pool] 关闭时归还 {typeof(T).Name} 失败。", exception); }
            InvokeDestroy(item);
        }

        private void InvokeDestroy(T item)
        {
            try
            {
                _onDestroy?.Invoke(item);
            }
            catch (Exception exception)
            {
                Log.Error($"[Pool] 销毁 {typeof(T).Name} 失败。", exception);
            }
        }

        private void ThrowIfClosed()
        {
            if (_isClosed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
        }

        #endregion
    }
}
