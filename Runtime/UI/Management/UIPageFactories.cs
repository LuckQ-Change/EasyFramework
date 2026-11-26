using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>保存由生成代码或热更入口显式登记的页面创建函数。</summary>
    public static class UIPageFactories
    {
        /// <summary>维护同一批工厂键和页面类型的双向索引。</summary>
        private sealed class Registry
        {
            internal readonly Dictionary<string, UIPageFactoryBinding> Factories;
            internal readonly Dictionary<Type, string> Keys;

            internal Registry()
            {
                Factories = new Dictionary<string, UIPageFactoryBinding>(StringComparer.Ordinal);
                Keys = new Dictionary<Type, string>();
            }

            internal Registry(Registry active)
            {
                Factories = new Dictionary<string, UIPageFactoryBinding>(active.Factories, StringComparer.Ordinal);
                Keys = new Dictionary<Type, string>(active.Keys);
            }

            internal void Add(string key, UIPageFactoryBinding binding)
            {
                Type viewType = binding.ViewType;
                if (Factories.TryGetValue(key, out UIPageFactoryBinding existing))
                {
                    if (existing.ViewType != viewType)
                        throw new InvalidOperationException($"工厂键「{key}」已与其他页面类型绑定。");
                    return;
                }
                if (Keys.ContainsKey(viewType))
                    throw new InvalidOperationException($"页面 {viewType.FullName} 已绑定另一个工厂键。");
                Factories.Add(key, binding);
                Keys.Add(viewType, key);
            }

            internal void Remove(string key, UIPageFactoryBinding binding)
            {
                // 仅注销这一批确实发布的工厂，保留同键同类型的既有登记。
                if (!Factories.TryGetValue(key, out UIPageFactoryBinding existing) ||
                    !ReferenceEquals(existing, binding)) return;
                Factories.Remove(key);
                Keys.Remove(binding.ViewType);
            }
        }

        /// <summary>暂存并统一发布一次热更发行登记的页面工厂。</summary>
        internal sealed class ReleaseRegistration : IDisposable
        {
            private readonly Registry _staged = new();
            private readonly int _generation = _sessionGeneration;
            private readonly int _threadId = Environment.CurrentManagedThreadId;
            private bool _isAccepting = true;
            private bool _isPublished;
            private bool _isDisposed;

            internal void Register<T>(string key, Func<T> factory) where T : UIView
            {
                if (Environment.CurrentManagedThreadId != _threadId)
                    throw new InvalidOperationException("请在 Unity 主线程登记热更页面工厂。");
                if (_isDisposed || _generation != _sessionGeneration)
                    throw new ObjectDisposedException(nameof(ReleaseRegistration));
                if (!_isAccepting)
                    throw new InvalidOperationException("请在热更入口返回前完成页面工厂登记。");

                key = NormalizeKey(key);
                _staged.Add(key, CreateBinding(factory));
            }

            internal async Task RunAsync(Func<Task> entry)
            {
                ReleaseRegistration previous = _entryRegistration.Value;
                _entryRegistration.Value = this;
                try { await entry(); }
                finally
                {
                    _isAccepting = false;
                    _entryRegistration.Value = previous;
                }
            }

            internal void Publish(Action confirm)
            {
                if (Environment.CurrentManagedThreadId != _threadId)
                    throw new InvalidOperationException("请在 Unity 主线程发布热更页面工厂。");
                if (_isDisposed || _generation != _sessionGeneration)
                    throw new ObjectDisposedException(nameof(ReleaseRegistration));
                if (_isAccepting || _isPublished)
                    throw new InvalidOperationException("热更入口结束后方可发布一批页面工厂。");

                // 在写最后可用发行前验证整个待发布表。确认失败时继续使用原表。
                var next = new Registry(_registry);
                foreach (var pair in _staged.Factories) next.Add(pair.Key, pair.Value);
                confirm();
                _registry = next;
                _isPublished = true;
            }

            /// <summary>放弃暂存登记，或撤回本批已经发布的工厂。</summary>
            public void Dispose()
            {
                if (_isDisposed) return;
                _isDisposed = true;
                if (_isPublished && _generation == _sessionGeneration)
                    foreach (var pair in _staged.Factories) _registry.Remove(pair.Key, pair.Value);
                _staged.Factories.Clear();
                _staged.Keys.Clear();
            }
        }

        private static readonly AsyncLocal<ReleaseRegistration> _entryRegistration = new();
        private static readonly HashSet<Assembly> _hotAssemblies = new();
        private static Registry _registry = new();
        private static int _sessionGeneration;

        #region 公共方法

        /// <summary>将页面工厂绑定到清单中的稳定工厂键。在 Unity 主线程登记。</summary>
        /// <typeparam name="T">工厂创建的具体页面类型。</typeparam>
        /// <param name="key">由 View 脚本 GUID 生成的工厂键。</param>
        /// <param name="factory">每次创建一个新 View 的函数。</param>
        public static void Register<T>(string key, Func<T> factory) where T : UIView
        {
            ReleaseRegistration entry = _entryRegistration.Value;
            if (entry != null)
            {
                entry.Register(key, factory);
                return;
            }
            lock (_hotAssemblies)
                if (_hotAssemblies.Contains(typeof(T).Assembly))
                    throw new InvalidOperationException("请在热更入口内登记热更页面工厂。");
            _registry.Add(NormalizeKey(key), CreateBinding(factory));
        }

        internal static void RecordHotAssembly(Assembly assembly)
        {
            lock (_hotAssemblies) _hotAssemblies.Add(assembly);
        }

        internal static string FindKey(Type type) => _registry.Keys.TryGetValue(type, out string key) ? key : null;

        internal static UIPageFactoryBinding Find(string key) =>
            key != null && _registry.Factories.TryGetValue(UIPageEntry.NormalizeGuid(key), out UIPageFactoryBinding binding)
                ? binding : null;

        #endregion

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _sessionGeneration++;
            _entryRegistration.Value = null;
            _registry = new Registry();
        }

        #endregion

        #region 私有逻辑

        private static string NormalizeKey(string key)
        {
            key = UIPageEntry.NormalizeGuid(key);
            if (!Guid.TryParseExact(key, "N", out _))
                throw new ArgumentException("页面工厂键必须是有效的脚本 GUID。", nameof(key));
            return key;
        }

        private static UIPageFactoryBinding CreateBinding<T>(Func<T> factory) where T : UIView
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return new UIPageFactoryBinding(typeof(T), factory);
        }

        #endregion
    }

    /// <summary>保存强类型页面工厂及其登记类型。</summary>
    internal sealed class UIPageFactoryBinding
    {
        private readonly Func<UIView> _create;

        internal Type ViewType { get; }

        internal UIPageFactoryBinding(Type viewType, Func<UIView> create)
        {
            ViewType = viewType;
            _create = create;
        }

        internal UIView Create()
        {
            UIView view = _create();
            if (view == null || view.GetType() != ViewType)
                throw new InvalidOperationException($"工厂未创建已登记的页面类型 {ViewType.FullName}。");
            return view;
        }
    }
}
