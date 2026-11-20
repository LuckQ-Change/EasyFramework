using System;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>持有一个资源。使用资源及其派生实例期间应保持租约有效。</summary>
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private AssetModule _owner;
        private AssetModule.Entry _entry;

        internal AssetModule.Entry Entry => _entry;
        public bool IsValid => _entry?.Loaded?.Asset is T asset && asset != null;
        public T Asset => IsValid ? (T)_entry.Loaded.Asset : throw new ObjectDisposedException(nameof(AssetLease<T>));

        internal AssetLease(AssetModule owner, AssetModule.Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_owner == null) return;
            _owner.VerifyThread();
            AssetModule owner = _owner;
            AssetModule.Entry entry = _entry;
            _owner = null;
            _entry = null;
            owner.Release(entry, keepIdle: true);
        }
    }
}
