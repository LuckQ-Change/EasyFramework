using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

namespace EasyFramework
{
    /// <summary>按包和地址共享加载，管理租约和有预算的空闲缓存。公共 API 在 Unity 主线程使用。</summary>
    public sealed class AssetModule : Module<AssetModule>, IFrameUpdate
    {
        public const string MainPackage = "main";
        private const double PressureCooldown = 10;

        internal sealed class Package
        {
            public readonly Func<IAssetLoader> CreateLoader;
            public IAssetLoader Loader;
            public bool Ready;
            public bool TrimNeeded;
            public bool StopFailed;
            public int PendingLoads;
            public Task<bool> Initialization;
            public Task Trim;
            public Task Stop;
            public TaskCompletionSource<bool> Shutdown;

            public Package(Func<IAssetLoader> createLoader) => CreateLoader = createLoader;
        }

        internal sealed class Entry
        {
            public readonly Package Package;
            public readonly string Key;
            public readonly string Address;
            public readonly CancellationTokenSource QueueCancellation = new CancellationTokenSource();
            public readonly TaskCompletionSource<LoadedAsset> Completion = NewCompletion<LoadedAsset>();
            public int Users;
            public bool Pending = true;
            public bool Loading;
            public bool Retired;
            public LoadedAsset Loaded;
            public long Bytes;
            public double IdleUntil;
            public LinkedListNode<Entry> IdleNode;

            public Entry(Package package, string key, string address)
            {
                Package = package;
                Key = key;
                Address = address;
            }
        }

        private sealed class Resident
        {
            public readonly CancellationTokenSource Stop = new();
            public AssetLease<UnityEngine.Object> Lease;
            public int Waiters;
        }

        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Dictionary<string, Package> _packages = new Dictionary<string, Package>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, Resident> _residents = new(StringComparer.Ordinal);
        private readonly LinkedList<Entry> _idle = new LinkedList<Entry>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private AssetCacheSettings _settings;
        private SemaphoreSlim _loadSlots;
        private SemaphoreSlim _packageSlots;
        private bool _stopped;
        private long _idleBytes;
        private double _nextMaintenance;
        private double _suppressCacheUntil;

        /// <summary>覆盖平台默认缓存策略。在模块初始化前调用。</summary>
        public void ConfigureCache(AssetCacheSettings settings)
        {
            VerifyThread();
            if (_loadSlots != null || _stopped) throw new InvalidOperationException("Configure the asset cache before module initialization.");
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>注册资源包的工厂。卸载后重进包时会调用工厂创建新后端。</summary>
        public void RegisterPackage(string alias, Func<IAssetLoader> createLoader)
        {
            VerifyThread();
            if (_stopped) throw new ObjectDisposedException(nameof(AssetModule));
            if (createLoader == null) throw new ArgumentNullException(nameof(createLoader));
            alias = NormalizeAlias(alias);
            if (_packages.ContainsKey(alias)) throw new InvalidOperationException($"Asset package '{alias}' is already registered.");
            _packages.Add(alias, new Package(createLoader));
        }

        public AssetScope CreateScope()
        {
            EnsureRunning();
            return new AssetScope(this);
        }

        public bool IsPackageRegistered(string alias)
        {
            VerifyThread();
            return _packages.ContainsKey(NormalizeAlias(alias));
        }

        /// <summary>只初始化包及其清单。调用方取消等待时，其他请求仍可复用包初始化。</summary>
        public async Task WarmPackageAsync(string alias, CancellationToken cancellationToken = default)
        {
            EnsureRunning();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
            cancellation.Token.ThrowIfCancellationRequested();
            await WaitForAsync(EnsurePackageAsync(GetPackage(NormalizeAlias(alias))), cancellation.Token);
        }

        /// <summary>使用 address 或 package:address 获取资源，失败时抛出异常。</summary>
        public async Task<AssetLease<T>> AcquireAsync<T>(string address, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            EnsureRunning();
            cancellationToken.ThrowIfCancellationRequested();
            (Package package, string key, string location) = ResolveAddress(address);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
            bool startLoad = !_entries.TryGetValue(key, out Entry entry);
            if (!startLoad && !entry.Pending &&
                (entry.Loaded?.Asset == null || (entry.IdleNode != null && entry.IdleUntil <= Time.realtimeSinceStartupAsDouble)))
            {
                Retire(entry);
                startLoad = true;
            }
            if (startLoad)
            {
                entry = new Entry(package, key, location);
                _entries.Add(key, entry);
            }

            RemoveFromIdle(entry);
            entry.Users++;
            if (startLoad)
            {
                package.PendingLoads++;
                _ = LoadEntryAsync(entry);
            }

            bool acquired = false;
            try
            {
                LoadedAsset loaded = await WaitForAsync(entry.Completion.Task, cancellation.Token);
                if (!(loaded.Asset is T asset) || asset == null)
                    throw new InvalidCastException($"Asset '{key}' cannot be used as {typeof(T).Name}.");

                acquired = true;
                return new AssetLease<T>(this, entry);
            }
            finally
            {
                if (!acquired) Release(entry, keepIdle: false);
            }
        }

        /// <summary>预热到普通空闲缓存。过期、预算不足或内存压力时可自动淘汰。</summary>
        public async Task WarmAsync<T>(string address, CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            using AssetLease<T> lease = await AcquireAsync<T>(address, cancellationToken);
        }

        /// <summary>为指定地址保留一份常驻租约。重复调用共享常驻状态。</summary>
        public async Task PinAsync(string address, CancellationToken cancellationToken = default)
        {
            EnsureRunning();
            cancellationToken.ThrowIfCancellationRequested();
            string key = ResolveAddress(address).Key;
            if (!_residents.TryGetValue(key, out Resident resident))
                _residents.Add(key, resident = new Resident());
            if (resident.Lease?.IsValid == true) return;
            resident.Lease?.Dispose();
            resident.Lease = null;

            resident.Waiters++;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(resident.Stop.Token, cancellationToken);
            AssetLease<UnityEngine.Object> lease = null;
            try
            {
                lease = await AcquireAsync<UnityEngine.Object>(key, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (resident.Lease == null)
                {
                    resident.Lease = lease;
                    lease = null;
                }
            }
            finally
            {
                lease?.Dispose();
                if (--resident.Waiters == 0 && resident.Lease == null)
                {
                    if (_residents.TryGetValue(key, out Resident current) && current == resident)
                        _residents.Remove(key);
                    resident.Stop.Dispose();
                }
            }
        }

        public bool IsResident(string address)
        {
            EnsureRunning();
            return _residents.TryGetValue(ResolveAddress(address).Key, out Resident resident) &&
                   resident.Lease?.IsValid == true;
        }

        /// <summary>取消常驻并归还租约。没有其他租约时，资源进入普通空闲回收流程。</summary>
        public bool Unpin(string address)
        {
            EnsureRunning();
            if (!_residents.Remove(ResolveAddress(address).Key, out Resident resident)) return false;
            ReleaseResident(resident);
            return true;
        }

        private static void ReleaseResident(Resident resident)
        {
            resident.Stop.Cancel();
            resident.Lease?.Dispose();
            resident.Lease = null;
            if (resident.Waiters == 0) resident.Stop.Dispose();
        }

        /// <summary>立即归还所有空闲句柄，并让当前已就绪的后端回收未使用资源。</summary>
        public Task TrimUnusedAsync()
        {
            EnsureRunning();
            EvictIdle(force: true);
            var trims = new List<Task>();
            foreach (Package package in _packages.Values)
            {
                if (!package.Ready || package.Stop != null) continue;
                package.TrimNeeded = true;
                trims.Add(TrimPackage(package));
            }
            return Task.WhenAll(trims);
        }

        /// <summary>清理空闲缓存并暂时停止保留空闲资源。可接入平台 SDK 的内存告警。</summary>
        public Task HandleMemoryPressureAsync()
        {
            EnsureRunning();
            _suppressCacheUntil = Time.realtimeSinceStartupAsDouble + PressureCooldown;
            return TrimUnusedAsync();
        }

        /// <summary>归还指定包的缓存和后端。还有使用者或加载时拒绝卸载。</summary>
        public Task UnloadPackageAsync(string alias)
        {
            EnsureRunning();
            Package package = GetPackage(NormalizeAlias(alias));
            if (package.Stop != null) return package.Stop;
            if (package.PendingLoads != 0 || package.Initialization != null)
                throw new InvalidOperationException($"Asset package '{alias}' is busy.");

            var cached = new List<Entry>();
            foreach (Entry entry in _entries.Values)
            {
                if (!ReferenceEquals(entry.Package, package)) continue;
                if (entry.Users != 0)
                    throw new InvalidOperationException($"Asset package '{alias}' still has active leases.");
                cached.Add(entry);
            }
            foreach (Entry entry in cached) Retire(entry);
            return StopPackage(package);
        }

        public AssetCacheSnapshot GetSnapshot()
        {
            VerifyThread();
            int pending = 0;
            int live = 0;
            int leases = 0;
            long activeBytes = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (entry.Pending) pending++;
                else if (entry.Users != 0)
                {
                    live++;
                    leases += entry.Users;
                    activeBytes += entry.Bytes;
                }
            }
            int residents = 0;
            long residentBytes = 0;
            foreach (Resident resident in _residents.Values)
            {
                if (resident.Lease?.IsValid != true) continue;
                residents++;
                residentBytes += resident.Lease.Entry.Bytes;
            }
            return new AssetCacheSnapshot(pending, live, leases, _idle.Count, activeBytes, _idleBytes, residents, residentBytes);
        }

        /// <summary>在 Unity 主线程读取已加载资源的活跃租约明细。</summary>
        /// <param name="destination">由调用方持有的列表。每次采集前清空并复用。</param>
        /// <param name="limit">最多复制的条目数。传入零时只清空列表。</param>
        public void CopyActiveLeaseUsage(List<AssetLeaseUsage> destination, int limit)
        {
            VerifyThread();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));

            destination.Clear();
            if (limit == 0) return;

            foreach (Entry entry in _entries.Values)
            {
                if (entry.Pending || entry.Users == 0) continue;
                destination.Add(new AssetLeaseUsage(entry.Key, entry.Users, entry.Bytes));
                if (destination.Count == limit) return;
            }
        }

        protected override void OnInit()
        {
            _settings ??= AssetCacheSettings.ForCurrentPlatform();
            _loadSlots = new SemaphoreSlim(_settings.MaxConcurrentLoads);
            _packageSlots = new SemaphoreSlim(_settings.MaxConcurrentPackageInitializations);
            Application.lowMemory += OnLowMemory;
        }

        void IFrameUpdate.Tick(FrameTime frameTime)
        {
            EvictIdle(force: false);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextMaintenance) return;
            _nextMaintenance = now + 1;
            foreach (Package package in _packages.Values)
            {
                if (package.TrimNeeded && package.Ready && package.Stop == null && package.Trim == null)
                    _ = Observe(TrimPackage(package), "Unused asset cleanup");
            }
        }

        protected override async Task OnShutdownAsync()
        {
            _stopped = true;
            Application.lowMemory -= OnLowMemory;
            foreach (Resident resident in _residents.Values) ReleaseResident(resident);
            _residents.Clear();
            _shutdown.Cancel();
            foreach (Entry entry in new List<Entry>(_entries.Values)) Retire(entry);
            var stops = new List<Task>();
            foreach (Package package in _packages.Values)
            {
                package.Shutdown = NewCompletion<bool>();
                stops.Add(package.Shutdown.Task);
                StopIfDrained(package);
            }
            _packages.Clear();
            try { await Task.WhenAll(stops); }
            finally
            {
                _shutdown.Dispose();
                _packageSlots?.Dispose();
                _loadSlots?.Dispose();
            }
        }

        internal void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Use EasyFramework assets on the Unity main thread.");
        }

        internal void Release(Entry entry, bool keepIdle)
        {
            if (entry.Retired) return;
            if (--entry.Users != 0) return;
            if (entry.Pending)
            {
                if (!entry.Loading) Retire(entry);
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!keepIdle || entry.Loaded?.Asset == null || _settings.IdleSeconds == 0 ||
                _settings.MaxIdleAssets == 0 || _settings.MaxIdleBytes == 0 || now < _suppressCacheUntil ||
                entry.Bytes > _settings.MaxIdleBytes)
            {
                Retire(entry);
                return;
            }

            entry.IdleUntil = now + _settings.IdleSeconds;
            entry.IdleNode = _idle.AddLast(entry);
            _idleBytes += entry.Bytes;
            EvictIdle(force: false);
        }

        private async Task LoadEntryAsync(Entry entry)
        {
            bool hasSlot = false;
            LoadedAsset loaded = null;
            try
            {
                CancellationToken token = entry.QueueCancellation.Token;
                await WaitForAsync(EnsurePackageAsync(entry.Package), token);
                await WaitForSlotAsync(_loadSlots, token);
                hasSlot = true;
                if (entry.Retired) return;
                entry.Loading = true;
                loaded = await entry.Package.Loader.LoadAsync(entry.Address);
                if (loaded?.Asset == null) throw new InvalidOperationException($"Asset '{entry.Key}' returned no object.");
                if (entry.Retired || entry.Users == 0) return;
                entry.Bytes = loaded.EstimatedBytes ?? Math.Max(0, Profiler.GetRuntimeMemorySizeLong(loaded.Asset));
                entry.Loaded = loaded;
                loaded = null;
                entry.Pending = false;
                entry.Completion.TrySetResult(entry.Loaded);
            }
            catch (Exception exception)
            {
                if (entry.Loading) entry.Package.TrimNeeded = true;
                if (!entry.Retired)
                {
                    Retire(entry, cancelQueued: false);
                    entry.Completion.TrySetException(exception);
                    _ = entry.Completion.Task.Exception;
                }
            }
            finally
            {
                if (loaded != null) ReleaseHandle(entry.Package, loaded);
                if (entry.Pending)
                {
                    entry.Pending = false;
                    Retire(entry, cancelQueued: false);
                    entry.Completion.TrySetCanceled();
                }
                entry.QueueCancellation.Dispose();
                if (hasSlot) _loadSlots.Release();
                entry.Package.PendingLoads--;
                StopIfDrained(entry.Package);
            }
        }

        private Task<bool> EnsurePackageAsync(Package package)
        {
            if (package.Stop != null || package.StopFailed)
                throw new InvalidOperationException("The asset package is unloading. Complete its unload before opening it again.");
            if (package.Ready) return Task.FromResult(true);
            if (package.Initialization != null) return package.Initialization;
            var completion = NewCompletion<bool>();
            package.Initialization = completion.Task;
            _ = InitializePackageAsync(package, completion);
            return completion.Task;
        }

        private async Task InitializePackageAsync(Package package, TaskCompletionSource<bool> completion)
        {
            // 资源包可能在任意调用点首次加载，后端工厂与初始化始终读取所属宿主。
            using var scope = Host.EnterScope();
            bool hasSlot = false;
            try
            {
                await WaitForSlotAsync(_packageSlots, _shutdown.Token);
                hasSlot = true;
                package.Loader ??= package.CreateLoader() ?? throw new InvalidOperationException("Asset loader factory returned null.");
                await package.Loader.InitializeAsync();
                package.Ready = true;
                completion.SetResult(true);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
                _ = completion.Task.Exception;
            }
            finally
            {
                if (hasSlot) _packageSlots.Release();
                package.Initialization = null;
                StopIfDrained(package);
            }
        }

        private Task TrimPackage(Package package)
        {
            if (package.Trim != null) return package.Trim;
            var completion = NewCompletion<bool>();
            package.Trim = completion.Task;
            _ = TrimPackageAsync(package, completion);
            return completion.Task;
        }

        private async Task TrimPackageAsync(Package package, TaskCompletionSource<bool> completion)
        {
            try
            {
                do
                {
                    package.TrimNeeded = false;
                    await package.Loader.TrimUnusedAsync();
                }
                while (package.TrimNeeded && !_stopped && package.Stop == null);
                completion.SetResult(true);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                package.Trim = null;
                StopIfDrained(package);
            }
        }

        private Task StopPackage(Package package)
        {
            if (package.Stop != null) return package.Stop;
            if (package.Loader == null) return Task.CompletedTask;
            var completion = NewCompletion<bool>();
            package.Stop = completion.Task;
            package.Ready = false;
            _ = StopPackageAsync(package, package.Trim, completion);
            return completion.Task;
        }

        private async Task StopPackageAsync(Package package, Task pendingTrim, TaskCompletionSource<bool> completion)
        {
            try
            {
                if (pendingTrim != null) await Observe(pendingTrim, "Unused asset cleanup");
                await package.Loader.ShutdownAsync();
                package.Loader = null;
                package.TrimNeeded = false;
                package.StopFailed = false;
                completion.SetResult(true);
            }
            catch (Exception exception)
            {
                package.StopFailed = true;
                completion.SetException(exception);
            }
            finally
            {
                package.Stop = null;
            }
        }

        private void StopIfDrained(Package package)
        {
            if (package.Shutdown == null || package.PendingLoads != 0 || package.Initialization != null || package.Trim != null)
                return;
            var completion = package.Shutdown;
            package.Shutdown = null;
            _ = FinishShutdownAsync(package, completion);
        }

        private async Task FinishShutdownAsync(Package package, TaskCompletionSource<bool> completion)
        {
            try
            {
                await StopPackage(package);
                completion.TrySetResult(true);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        private void OnLowMemory()
        {
            if (!_stopped) _ = Observe(HandleMemoryPressureAsync(), "Low-memory cleanup");
        }

        private void EvictIdle(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            while (_idle.First is LinkedListNode<Entry> node &&
                   (force || node.Value.IdleUntil <= now || _idle.Count > _settings.MaxIdleAssets || _idleBytes > _settings.MaxIdleBytes))
                Retire(node.Value);
        }

        private void Retire(Entry entry, bool cancelQueued = true)
        {
            if (entry.Retired) return;
            entry.Retired = true;
            RemoveFromIdle(entry);
            _entries.Remove(entry.Key);
            LoadedAsset loaded = entry.Loaded;
            entry.Loaded = null;
            if (loaded != null) ReleaseHandle(entry.Package, loaded);
            if (cancelQueued && entry.Pending && !entry.Loading) entry.QueueCancellation.Cancel();
        }

        private void RemoveFromIdle(Entry entry)
        {
            if (entry.IdleNode == null) return;
            _idle.Remove(entry.IdleNode);
            entry.IdleNode = null;
            _idleBytes -= entry.Bytes;
        }

        private static void ReleaseHandle(Package package, LoadedAsset loaded)
        {
            package.TrimNeeded = true;
            try { loaded.Dispose(); }
            catch (Exception exception) { Log.Error("Asset handle release failed.", exception); }
        }

        private void EnsureRunning()
        {
            VerifyThread();
            if (_stopped) throw new ObjectDisposedException(nameof(AssetModule));
            if (_loadSlots == null) throw new InvalidOperationException("AssetModule has not been initialized.");
        }

        private (Package Package, string Key, string Address) ResolveAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Asset address is required.", nameof(address));
            int delimiter = address.IndexOf(':');
            string alias = delimiter < 0 ? MainPackage : NormalizeAlias(address.Substring(0, delimiter));
            string location = (delimiter < 0 ? address : address.Substring(delimiter + 1)).Trim();
            if (location.Length == 0 || location.Contains(":"))
                throw new ArgumentException("Use address or package:address.", nameof(address));
            return (GetPackage(alias), $"{alias}:{location}", location);
        }

        private Package GetPackage(string alias) =>
            _packages.TryGetValue(alias, out Package package)
                ? package
                : throw new KeyNotFoundException($"Asset package '{alias}' is not registered.");

        internal static string NormalizeAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentException("Asset package alias is required.", nameof(alias));
            alias = alias.Trim();
            if (alias.Contains(":")) throw new ArgumentException("Asset package alias cannot contain ':'.", nameof(alias));
            return alias;
        }

        private static TaskCompletionSource<T> NewCompletion<T>() =>
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static Task WaitForSlotAsync(SemaphoreSlim slots, CancellationToken token)
        {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
            return WaitForWebGLSlotAsync(slots, token);
#else
            return slots.WaitAsync(token);
#endif
        }

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        private static async Task WaitForWebGLSlotAsync(SemaphoreSlim slots, CancellationToken token)
        {
            // 槽位用尽时由 Unity 主线程重试，避免异步信号量依赖线程池唤醒排队操作。
            token.ThrowIfCancellationRequested();
            while (!slots.Wait(0))
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
            }
        }
#endif

        private static async Task<T> WaitForAsync<T>(Task<T> operation, CancellationToken token)
        {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
            // WebGL 的 Task.WhenAny 会把异步延续的完成通知交给不可用的线程池。
            // 在 Unity 主线程等待结果，取消只中断当前调用方，不影响共享加载。
            while (!operation.IsCompleted && !token.IsCancellationRequested)
                await Task.Yield();
#else
            if (!operation.IsCompleted)
            {
                var canceled = NewCompletion<bool>();
                using (token.Register(() => canceled.TrySetResult(true)))
                    await Task.WhenAny(operation, canceled.Task);
            }
#endif
            token.ThrowIfCancellationRequested();
            return await operation;
        }

        private static async Task Observe(Task task, string operation)
        {
            try { await task; }
            catch (Exception exception) { Log.Error($"{operation} failed.", exception); }
        }
    }

    /// <summary>当前活跃租约、加载请求及空闲缓存的估计大小。</summary>
    public readonly struct AssetCacheSnapshot
    {
        public int PendingAssets { get; }
        public int ActiveAssets { get; }
        public int ActiveLeases { get; }
        public int IdleAssets { get; }
        public int ResidentAssets { get; }
        public long ActiveEstimatedBytes { get; }
        public long IdleEstimatedBytes { get; }
        public long ResidentEstimatedBytes { get; }

        internal AssetCacheSnapshot(int pending, int active, int leases, int idle, long activeBytes, long idleBytes,
            int residents, long residentBytes)
        {
            PendingAssets = pending;
            ActiveAssets = active;
            ActiveLeases = leases;
            IdleAssets = idle;
            ResidentAssets = residents;
            ActiveEstimatedBytes = activeBytes;
            IdleEstimatedBytes = idleBytes;
            ResidentEstimatedBytes = residentBytes;
        }
    }

    /// <summary>已加载资源在采集时持有的租约明细。</summary>
    public readonly struct AssetLeaseUsage
    {
        /// <summary>规范化的资源包别名与资源地址。</summary>
        public string Address { get; }

        /// <summary>当前持有该资源的租约数，包含常驻租约。</summary>
        public int LeaseCount { get; }

        /// <summary>该资源的估算字节数。</summary>
        public long EstimatedBytes { get; }

        internal AssetLeaseUsage(string address, int leaseCount, long estimatedBytes)
        {
            Address = address;
            LeaseCount = leaseCount;
            EstimatedBytes = estimatedBytes;
        }
    }
}
