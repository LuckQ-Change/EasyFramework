using System;
using System.Threading;

namespace EasyFramework
{
    /// <summary>表示一次资源持有；Dispose 后不得继续使用该租约。</summary>
    public sealed class AssetLease<T> : IDisposable where T : class
    {
        private AssetModule _owner;
        private readonly string _location;
        private readonly int _generation;

        public T Asset { get; }
        public bool IsValid => Asset != null && _owner != null;

        internal AssetLease(
            AssetModule owner,
            string location,
            int generation,
            T asset)
        {
            _owner = owner;
            _location = location;
            _generation = generation;
            Asset = asset;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_location, _generation);
        }
    }
}
