using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>Pure C# generated-reference binding. It is never attached to a prefab.</summary>
    public abstract class EasyUIBinding : IDisposable
    {
        public EasyUIDisplay Display { get; private set; }
        public Transform Root => Display == null ? null : Display.transform;
        public UIBindingContext Context => Display == null ? null : Display.BindingContext;
        private readonly List<IDisposable> _resourceLeases = new List<IDisposable>();

        internal void Initialize(EasyUIDisplay display)
        {
            Display = display ?? throw new ArgumentNullException(nameof(display));
            OnBind();
        }

        public T Find<T>(string relativePath) where T : Component
        {
            Transform target = string.IsNullOrEmpty(relativePath) ? Root : Root?.Find(relativePath);
            return target == null ? null : target.GetComponent<T>();
        }

        protected virtual void OnBind() { }
        protected virtual void OnUnbind() { }

        public async Task<T> LoadAsync<T>(string location) where T : class
        {
            AssetModule assets = AssetModule.Instance;
            if (assets == null || !assets.IsReady)
                throw new InvalidOperationException("AssetModule must be ready before a UI binding loads resources.");
            AssetLease<T> lease = await assets.AcquireAsync<T>(location);
            if (lease == null || lease.Asset == null) return null;
            if (Display == null)
            {
                lease.Dispose();
                return null;
            }
            _resourceLeases.Add(lease);
            return lease.Asset;
        }

        public void Dispose()
        {
            try { OnUnbind(); }
            finally
            {
                foreach (IDisposable lease in _resourceLeases) lease?.Dispose();
                _resourceLeases.Clear();
                Display = null;
            }
        }
    }

    public sealed class EmptyEasyUIBinding : EasyUIBinding { }
}
