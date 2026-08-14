using System;
using System.Collections.Generic;

namespace EasyFramework.UI
{
    public interface IReadOnlyReactiveProperty
    {
        Type ValueType { get; }
        object BoxedValue { get; }
        IDisposable Subscribe(Action<object> listener, bool notifyImmediately = true);
    }

    public interface IReactiveProperty : IReadOnlyReactiveProperty
    {
        bool TrySetValue(object value);
    }

    public sealed class ReactiveProperty<T> : IReactiveProperty, IDisposable
    {
        private readonly List<Action<T>> _listeners = new List<Action<T>>();
        private readonly IEqualityComparer<T> _comparer;
        private T _value;
        private int _dispatchDepth;
        private bool _requiresCompaction;
        private bool _disposed;

        public ReactiveProperty(T initialValue = default(T), IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get => _value;
            set
            {
                ThrowIfDisposed();
                if (_comparer.Equals(_value, value)) return;
                _value = value;
                Notify(value);
            }
        }

        public Type ValueType => typeof(T);
        public object BoxedValue => _value;

        public IDisposable Subscribe(Action<T> listener, bool notifyImmediately = true)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            ThrowIfDisposed();
            _listeners.Add(listener);
            if (notifyImmediately) listener(_value);
            return new Subscription(this, listener);
        }

        IDisposable IReadOnlyReactiveProperty.Subscribe(Action<object> listener, bool notifyImmediately)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            return Subscribe(value => listener(value), notifyImmediately);
        }

        public bool TrySetValue(object value)
        {
            if (!UIValueConverter.TryConvert(value, typeof(T), out var converted)) return false;
            Value = (T)converted;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listeners.Clear();
        }

        private void Notify(T value)
        {
            _dispatchDepth++;
            int count = _listeners.Count;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var listener = _listeners[i];
                    if (listener == null) continue;
                    try { listener(value); }
                    catch (Exception exception) { Log.Error($"[UI] Reactive listener failed: {exception}"); }
                }
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0 && _requiresCompaction) Compact();
            }
        }

        private void Unsubscribe(Action<T> listener)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i] != listener) continue;
                if (_dispatchDepth > 0)
                {
                    _listeners[i] = null;
                    _requiresCompaction = true;
                }
                else
                {
                    _listeners.RemoveAt(i);
                }
                return;
            }
        }

        private void Compact()
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i] == null) _listeners.RemoveAt(i);
            }
            _requiresCompaction = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ReactiveProperty<T>));
        }

        private sealed class Subscription : IDisposable
        {
            private ReactiveProperty<T> _owner;
            private Action<T> _listener;

            public Subscription(ReactiveProperty<T> owner, Action<T> listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;
                _owner = null;
                var listener = _listener;
                _listener = null;
                owner.Unsubscribe(listener);
            }
        }
    }
}
