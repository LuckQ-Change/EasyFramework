using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class EventModule : ModuleSingleton<EventModule>
    {
        private interface IEventChannel
        {
            void Clear();
        }

        private sealed class EventChannel<T> : IEventChannel where T : IEvent
        {
            private readonly List<Action<T>> _handlers = new List<Action<T>>();
            private int _dispatchDepth;
            private bool _needsCompaction;

            public void Subscribe(Action<T> handler)
            {
                _handlers.Add(handler);
            }

            public void Unsubscribe(Action<T> handler)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i] != handler) continue;
                    if (_dispatchDepth > 0)
                    {
                        _handlers[i] = null;
                        _needsCompaction = true;
                    }
                    else
                    {
                        _handlers.RemoveAt(i);
                    }
                    return;
                }
            }

            public void Dispatch(T evt)
            {
                _dispatchDepth++;
                int count = _handlers.Count;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var handler = _handlers[i];
                        if (handler == null) continue;
                        try
                        {
                            handler(evt);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[Event] handler error for {typeof(T).Name}.", ex);
                        }
                    }
                }
                finally
                {
                    _dispatchDepth--;
                    if (_dispatchDepth == 0 && _needsCompaction) Compact();
                }
            }

            public void Clear()
            {
                if (_dispatchDepth == 0)
                {
                    _handlers.Clear();
                    _needsCompaction = false;
                    return;
                }

                for (int i = 0; i < _handlers.Count; i++) _handlers[i] = null;
                _needsCompaction = true;
            }

            private void Compact()
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i] == null) _handlers.RemoveAt(i);
                }
                _needsCompaction = false;
            }
        }

        private readonly Dictionary<Type, IEventChannel> _channels =
            new Dictionary<Type, IEventChannel>();

        public void Subscribe<T>(Action<T> handler) where T : IEvent
        {
            if (handler == null) return;
            GetOrCreateChannel<T>().Subscribe(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IEvent
        {
            if (handler == null) return;
            if (_channels.TryGetValue(typeof(T), out var channel))
            {
                ((EventChannel<T>)channel).Unsubscribe(handler);
            }
        }

        public void Dispatch<T>(T evt) where T : IEvent
        {
            if (_channels.TryGetValue(typeof(T), out var channel))
            {
                ((EventChannel<T>)channel).Dispatch(evt);
            }
        }

        public void Clear<T>() where T : IEvent
        {
            if (!_channels.TryGetValue(typeof(T), out var channel)) return;
            channel.Clear();
            _channels.Remove(typeof(T));
        }

        public void ClearAll()
        {
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
        }

        protected override void OnShutdown()
        {
            ClearAll();
        }

        private EventChannel<T> GetOrCreateChannel<T>() where T : IEvent
        {
            var type = typeof(T);
            if (_channels.TryGetValue(type, out var channel)) return (EventChannel<T>)channel;

            var created = new EventChannel<T>();
            _channels.Add(type, created);
            return created;
        }
    }
}
