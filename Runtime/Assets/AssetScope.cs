using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>页面或关卡的资源所有权。销毁时取消等待并归还已获取的资源。</summary>
    public sealed class AssetScope : IDisposable
    {
        private readonly AssetModule _module;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Dictionary<AssetModule.Entry, IDisposable> _leases = new Dictionary<AssetModule.Entry, IDisposable>();
        private bool _disposed;

        internal AssetScope(AssetModule module) => _module = module;

        internal void AddUsage(ref int leases, ref long estimatedBytes)
        {
            foreach (AssetModule.Entry entry in _leases.Keys)
            {
                if (entry.Retired) continue;
                leases++;
                estimatedBytes += entry.Bytes;
            }
        }

        public async Task<T> LoadAsync<T>(string address, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            _module.VerifyThread();
            if (_disposed) throw new ObjectDisposedException(nameof(AssetScope));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
            AssetLease<T> lease = await _module.AcquireAsync<T>(address, cancellation.Token);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                T asset = lease.Asset;
                if (!_leases.ContainsKey(lease.Entry))
                {
                    _leases.Add(lease.Entry, lease);
                    lease = null;
                }
                return asset;
            }
            finally { lease?.Dispose(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _module.VerifyThread();
            _disposed = true;
            try { _stop.Cancel(); }
            catch (Exception exception) { Log.Error("[Assets] 取消作用域请求失败。", exception); }
            _stop.Dispose();
            foreach (IDisposable lease in _leases.Values)
            {
                try { lease.Dispose(); }
                catch (Exception exception) { Log.Error("[Assets] 归还作用域租约失败。", exception); }
            }
            _leases.Clear();
        }
    }
}
