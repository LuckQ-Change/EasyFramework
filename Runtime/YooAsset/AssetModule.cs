using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyFramework
{
    public class AssetModule : ModuleSingleton<AssetModule>
    {
        private sealed class AssetEntry
        {
            public Task<object> LoadTask;
            public int ReferenceCount;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, AssetEntry> _entries =
            new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        private IAssetLoader _loader;
        private Task<bool> _initializeTask;
        private int _generation;

        public bool IsReady { get; private set; }

        public void SetLoader(IAssetLoader loader)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            lock (_sync)
            {
                if (IsReady || _initializeTask != null || _entries.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Cannot replace the asset loader after initialization. Shutdown the module first.");
                }
                _loader = loader;
                _generation++;
            }
        }

        public Task<bool> InitializeAsync()
        {
            lock (_sync)
            {
                if (IsReady) return Task.FromResult(true);
                if (_loader == null)
                {
                    Log.Error("[Asset] no loader assigned, call SetLoader first.");
                    return Task.FromResult(false);
                }
                if (_initializeTask != null) return _initializeTask;

                var loader = _loader;
                int generation = _generation;
                _initializeTask = InitializeCoreAsync(loader, generation);
                return _initializeTask;
            }
        }

        public async Task<T> LoadAsync<T>(string location) where T : class
        {
            var result = await AcquireAssetAsync<T>(location);
            return result.Asset;
        }

        public async Task<AssetLease<T>> AcquireAsync<T>(string location) where T : class
        {
            var result = await AcquireAssetAsync<T>(location);
            return result.Asset == null
                ? null
                : new AssetLease<T>(this, location, result.Generation, result.Asset);
        }

        public void Release(string location)
        {
            int generation;
            lock (_sync) generation = _generation;
            Release(location, generation);
        }

        internal void Release(string location, int generation)
        {
            if (string.IsNullOrEmpty(location)) return;

            IAssetLoader loader = null;
            lock (_sync)
            {
                if (generation != _generation) return;
                if (!_entries.TryGetValue(location, out var entry)) return;

                entry.ReferenceCount--;
                if (entry.ReferenceCount > 0) return;
                _entries.Remove(location);
                loader = _loader;
            }
            loader?.Release(location);
        }

        public void ReleaseAll()
        {
            IAssetLoader loader;
            lock (_sync)
            {
                foreach (var entry in _entries.Values)
                {
                    if (!entry.LoadTask.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Cannot release all assets while loads are still pending.");
                    }
                }
                _entries.Clear();
                _generation++;
                loader = _loader;
            }
            loader?.ReleaseAll();
        }

        protected override void OnShutdown()
        {
            IAssetLoader loader;
            List<(string Location, Task<object> Task)> pending = null;
            lock (_sync)
            {
                foreach (var pair in _entries)
                {
                    if (pair.Value.LoadTask.IsCompleted) continue;
                    if (pending == null) pending = new List<(string, Task<object>)>();
                    pending.Add((pair.Key, pair.Value.LoadTask));
                }
                _entries.Clear();
                _generation++;
                loader = _loader;
                _loader = null;
                _initializeTask = null;
                IsReady = false;
            }
            if (loader == null) return;
            try { loader.ReleaseAll(); }
            catch (Exception ex) { Log.Error($"[Asset] release all failed during shutdown: {ex}"); }
            if (pending == null) return;

            foreach (var item in pending)
            {
                _ = ReleaseWhenCompletedAsync(loader, item.Location, item.Task);
            }
        }

        private async Task<bool> InitializeCoreAsync(IAssetLoader loader, int generation)
        {
            // Prevent a synchronously completed loader from racing the assignment of _initializeTask.
            await Task.Yield();
            bool succeeded;
            try
            {
                succeeded = await loader.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"[Asset] initialize failed: {ex}");
                succeeded = false;
            }

            lock (_sync)
            {
                if (generation == _generation && ReferenceEquals(loader, _loader))
                {
                    IsReady = succeeded;
                    _initializeTask = null;
                }
            }
            return succeeded;
        }

        private async Task<(T Asset, int Generation)> AcquireAssetAsync<T>(string location)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Asset location is required.", nameof(location));
            }

            AssetEntry entry;
            IAssetLoader loader;
            int generation;
            lock (_sync)
            {
                if (!IsReady || _loader == null)
                {
                    Log.Error($"[Asset] not initialized, cannot load {location}");
                    return (null, _generation);
                }

                loader = _loader;
                generation = _generation;
                if (!_entries.TryGetValue(location, out entry))
                {
                    entry = new AssetEntry
                    {
                        LoadTask = LoadObjectAsync<T>(loader, location),
                    };
                    _entries.Add(location, entry);
                }
                entry.ReferenceCount++;
            }

            object loaded;
            try
            {
                loaded = await entry.LoadTask;
            }
            catch
            {
                Release(location, generation);
                throw;
            }

            bool stale;
            lock (_sync)
            {
                stale = generation != _generation || !ReferenceEquals(loader, _loader);
            }
            if (stale)
            {
                loader.Release(location);
                return (null, generation);
            }

            if (loaded is T typed) return (typed, generation);

            Log.Error($"[Asset] loaded asset type mismatch or missing: {location} ({typeof(T).Name})");
            Release(location, generation);
            return (null, generation);
        }

        private static async Task<object> LoadObjectAsync<T>(IAssetLoader loader, string location)
            where T : class
        {
            return await loader.LoadAsync<T>(location);
        }

        private static async Task ReleaseWhenCompletedAsync(
            IAssetLoader loader,
            string location,
            Task<object> loadTask)
        {
            try { await loadTask; }
            catch { }
            try { loader.Release(location); }
            catch (Exception ex) { Log.Error($"[Asset] late release failed for {location}: {ex}"); }
        }
    }
}
