using System;
using System.Collections.Generic;

namespace EasyFramework.UI
{
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _disposed;

        public void Add(IDisposable disposable)
        {
            if (disposable == null) return;
            if (_disposed)
            {
                disposable.Dispose();
                return;
            }
            _items.Add(disposable);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                try { _items[i]?.Dispose(); }
                catch (Exception exception) { Log.Error("[UI] Dispose binding failed.", exception); }
            }
            _items.Clear();
        }
    }

    internal sealed class ActionDisposable : IDisposable
    {
        private Action _dispose;

        public ActionDisposable(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            var dispose = _dispose;
            if (dispose == null) return;
            _dispose = null;
            dispose();
        }
    }
}
