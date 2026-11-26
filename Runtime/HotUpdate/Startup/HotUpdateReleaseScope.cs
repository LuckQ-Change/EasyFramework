using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyFramework.UI;

namespace EasyFramework
{
    /// <summary>持有单个候选发行登记的页面工厂、资源与订阅。</summary>
    internal sealed class HotUpdateReleaseScope : IDisposable
    {
        private readonly UIPageFactories.ReleaseRegistration _pages = new();
        private readonly List<IDisposable> _owned = new();
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private bool _isDisposed;

        internal Task RunEntryAsync(Func<Task> entry) => _pages.RunAsync(entry);

        internal void Publish(Action confirm) => _pages.Publish(confirm);

        internal void RegisterPageFactory<T>(string factoryKey, Func<T> factory) where T : UIView =>
            _pages.Register(factoryKey, factory);

        internal T Own<T>(T handle) where T : class, IDisposable
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("请在 Unity 主线程把资源交给热更发行作用域。");
            if (_isDisposed)
            {
                DisposeHandle(handle);
                throw new ObjectDisposedException(nameof(HotUpdateReleaseScope));
            }

            foreach (IDisposable owned in _owned)
                if (ReferenceEquals(owned, handle)) return handle;
            _owned.Add(handle);
            return handle;
        }

        internal void OnDispose(Action cleanup)
        {
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            Own(new ActionDisposable(cleanup));
        }

        /// <summary>撤回发行工厂，按登记的逆序清理资源和订阅。</summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            DisposeHandle(_pages);
            for (int index = _owned.Count - 1; index >= 0; index--)
                DisposeHandle(_owned[index]);
            _owned.Clear();
        }

        private static void DisposeHandle(IDisposable handle)
        {
            try { handle.Dispose(); }
            catch (Exception exception) { Log.Error("[HotUpdate] 释放发行作用域登记失败。", exception); }
        }
    }
}
