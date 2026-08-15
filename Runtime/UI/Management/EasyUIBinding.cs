using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>Runtime access to references serialized on EasyUIReference markers.</summary>
    public class EasyUIBinding : IDisposable
    {
        public EasyUIDisplay Display { get; private set; }
        public Transform Root => Display == null ? null : Display.transform;
        public UIBindingContext Context => Display == null ? null : Display.BindingContext;
        private readonly List<IDisposable> _resourceLeases = new List<IDisposable>();
        private readonly Dictionary<string, EasyUIReferenceEntry> _references =
            new Dictionary<string, EasyUIReferenceEntry>(StringComparer.Ordinal);

        internal void Initialize(EasyUIDisplay display)
        {
            Display = display ?? throw new ArgumentNullException(nameof(display));
            CollectReferences();
            OnBind();
        }

        public T Get<T>(string key) where T : Component
        {
            if (!_references.TryGetValue(key ?? string.Empty, out EasyUIReferenceEntry entry))
                throw new KeyNotFoundException($"UI reference '{key}' was not found on {Display?.name}.");
            if (entry.Target is T typed) return typed;
            throw new InvalidCastException(
                $"UI reference '{key}' is {entry.Target?.GetType().Name ?? "null"}, expected {typeof(T).Name}.");
        }

        public bool TryGet<T>(string key, out T value) where T : Component
        {
            value = null;
            if (!_references.TryGetValue(key ?? string.Empty, out EasyUIReferenceEntry entry)) return false;
            value = entry.Target as T;
            return value != null;
        }

        public string GetResourceLocation(string key)
        {
            return _references.TryGetValue(key ?? string.Empty, out EasyUIReferenceEntry entry)
                ? entry.ResourceLocation
                : null;
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
                _references.Clear();
                Display = null;
            }
        }

        private void CollectReferences()
        {
            foreach (EasyUIReference marker in Display.GetComponentsInChildren<EasyUIReference>(true))
            {
                foreach (EasyUIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null || string.IsNullOrWhiteSpace(entry.Key)) continue;
                    string key = entry.Key.Trim();
                    if (_references.ContainsKey(key))
                        throw new InvalidOperationException($"Duplicate UI reference key '{key}' on {Display.name}.");
                    _references.Add(key, entry);
                }
            }
        }
    }
}
