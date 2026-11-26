using System;
using System.Collections.Generic;

namespace EasyFramework
{
    /// <summary>在所属宿主内同步派发类型化事件，并隔离单个订阅者异常。</summary>
    public class EventModule : Module
    {
        /// <summary>提供按类型释放订阅通道的共同入口。</summary>
        private interface IEventChannel
        {
            /// <summary>使通道当前的订阅全部失效。</summary>
            void Clear();
        }

        /// <summary>维护同一事件类型的订阅顺序和嵌套派发。</summary>
        private sealed class EventChannel<T> : IEventChannel where T : IEvent
        {
            /// <summary>持有一次订阅的所有权，同一委托的不同订阅各自独立。</summary>
            private sealed class Subscription : IDisposable
            {
                private EventChannel<T> _channel;

                internal Subscription(EventChannel<T> channel, Action<T> handler)
                {
                    _channel = channel;
                    Handler = handler;
                }

                internal Action<T> Handler { get; private set; }

                /// <summary>解除此句柄对应的订阅，重复释放无效。</summary>
                public void Dispose()
                {
                    EventChannel<T> channel = _channel;
                    if (channel == null) return;
                    Detach();
                    channel.Remove(this);
                }

                internal void Detach()
                {
                    _channel = null;
                    Handler = null;
                }
            }

            private readonly List<Subscription> _subscriptions = new();
            private int _dispatchDepth;
            private bool _hasRemovedSubscriptions;

            internal IDisposable Subscribe(Action<T> handler)
            {
                var subscription = new Subscription(this, handler);
                _subscriptions.Add(subscription);
                return subscription;
            }

            private void Remove(Subscription subscription)
            {
                // 派发中只让订阅失效，保留索引直到最外层派发退出。
                if (_dispatchDepth > 0) _hasRemovedSubscriptions = true;
                else _subscriptions.Remove(subscription);
            }

            internal void Dispatch(T evt)
            {
                _dispatchDepth++;
                int count = _subscriptions.Count;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        Action<T> handler = _subscriptions[i].Handler;
                        if (handler == null) continue;
                        try
                        {
                            handler(evt);
                        }
                        catch (Exception exception)
                        {
                            Log.Error($"[Event] 事件 {typeof(T).Name} 的订阅回调失败。", exception);
                        }
                    }
                }
                finally
                {
                    _dispatchDepth--;
                    if (_dispatchDepth == 0 && _hasRemovedSubscriptions)
                    {
                        _subscriptions.RemoveAll(static subscription => subscription.Handler == null);
                        _hasRemovedSubscriptions = false;
                    }
                }
            }

            void IEventChannel.Clear()
            {
                // 批量清理也须释放句柄上的回调，外部保存的旧句柄随后可以安全 Dispose。
                foreach (Subscription subscription in _subscriptions) subscription.Detach();
                if (_dispatchDepth > 0) _hasRemovedSubscriptions = true;
                else _subscriptions.Clear();
            }
        }

        private readonly Dictionary<Type, IEventChannel> _channels = new();
        private bool _isStopped;

        #region 公共方法

        /// <summary>按注册顺序接收指定类型的事件。订阅、释放与派发在同一宿主线程进行。</summary>
        /// <typeparam name="T">要接收的事件类型。</typeparam>
        /// <param name="handler">收到事件时同步执行的回调。</param>
        /// <returns>解除此次订阅的句柄，支持重复释放。</returns>
        public IDisposable Subscribe<T>(Action<T> handler) where T : IEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_isStopped) throw new ObjectDisposedException(nameof(EventModule));
            return GetOrCreateChannel<T>().Subscribe(handler);
        }

        /// <summary>向此宿主中指定类型的订阅者同步派发事件。</summary>
        /// <typeparam name="T">事件的订阅类型。</typeparam>
        /// <param name="evt">本次派发的事件数据。</param>
        public void Dispatch<T>(T evt) where T : IEvent
        {
            if (_channels.TryGetValue(typeof(T), out var channel))
            {
                ((EventChannel<T>)channel).Dispatch(evt);
            }
        }

        /// <summary>解除指定类型的全部订阅，使对应的句柄失效。</summary>
        /// <typeparam name="T">要清理的事件类型。</typeparam>
        public void Clear<T>() where T : IEvent
        {
            if (!_channels.TryGetValue(typeof(T), out var channel)) return;
            channel.Clear();
            _channels.Remove(typeof(T));
        }

        /// <summary>解除此宿主中的全部事件订阅。</summary>
        public void ClearAll()
        {
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
        }

        #endregion

        #region 生命周期

        protected override void OnShutdown()
        {
            _isStopped = true;
            ClearAll();
        }

        #endregion

        #region 私有逻辑

        private EventChannel<T> GetOrCreateChannel<T>() where T : IEvent
        {
            var type = typeof(T);
            if (_channels.TryGetValue(type, out var channel)) return (EventChannel<T>)channel;

            var created = new EventChannel<T>();
            _channels.Add(type, created);
            return created;
        }

        #endregion
    }
}
