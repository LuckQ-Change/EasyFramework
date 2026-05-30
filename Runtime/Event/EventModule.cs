using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class EventModule : ModuleSingleton<EventModule>
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(Action<T> handler) where T : IEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var exist))
            {
                _handlers[type] = Delegate.Combine(exist, handler);
            }
            else
            {
                _handlers[type] = handler;
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var exist)) return;

            var remain = Delegate.Remove(exist, handler);
            if (remain == null)
            {
                _handlers.Remove(type);
            }
            else
            {
                _handlers[type] = remain;
            }
        }

        public void Dispatch<T>(T evt) where T : IEvent
        {
            if (!_handlers.TryGetValue(typeof(T), out var del)) return;

            var list = del.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T>)list[i])(evt);
                }
                catch (Exception ex)
                {
                    Log.Error($"[Event] handler error for {typeof(T).Name}: {ex}");
                }
            }
        }

        public void Clear<T>() where T : IEvent
        {
            _handlers.Remove(typeof(T));
        }

        public void ClearAll()
        {
            _handlers.Clear();
        }

        protected override void OnShutdown()
        {
            _handlers.Clear();
        }
    }
}
