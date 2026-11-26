using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>在本地宿主上准备一套发行、执行热更入口，并按阶段记录可恢复失败。</summary>
    internal sealed class HotUpdateStartup
    {
        /// <summary>由 AssetModule 使用的受管包视图，后端生命周期归启动协调器。</summary>
        private sealed class GatedLoader : IAssetLoader
        {
            private readonly HotUpdateStartup _owner;
            private readonly string _alias;

            internal GatedLoader(HotUpdateStartup owner, string alias)
            {
                _owner = owner;
                _alias = alias;
            }

            /// <summary>检查热更入口是否已完成。</summary>
            /// <returns>包可用时完成的任务。</returns>
            public Task InitializeAsync()
            {
                _owner.RequireOpenPackage(_alias);
                return Task.CompletedTask;
            }

            /// <summary>从已开放的发行版本读取资源。</summary>
            /// <param name="address">包内资源地址。</param>
            /// <returns>底层资源句柄的所有权。</returns>
            public Task<LoadedAsset> LoadAsync(string address) => _owner.RequireOpenPackage(_alias).LoadAsync(address);

            /// <summary>清理受管包中暂未使用的资源。</summary>
            /// <returns>清理结束的任务。</returns>
            public Task TrimUnusedAsync() => _owner._loaders[_alias].TrimUnusedAsync();

            /// <summary>释放此包视图，实际后端随热更启动宿主关闭。</summary>
            /// <returns>视图已关闭的任务。</returns>
            public Task ShutdownAsync() => Task.CompletedTask;
        }

        private static readonly AsyncLocal<HotUpdateStartup> _entryOwner = new();
        private static int _hasProcessLoad;
        private readonly FrameworkStartupConfig _startup;
        private readonly HotUpdateStartupConfig _configuration;
        private readonly ModuleManager _host;
        private readonly HotUpdateReleaseSelection _releaseSelection;
        private readonly Dictionary<string, IHotUpdateAssetLoader> _loaders = new(StringComparer.Ordinal);
        private HotUpdatePlan _plan;
        private HotUpdateReleaseScope _releaseScope;
        private bool _isRestartRequired;
        private bool _isClosing;
        private bool _isExecutingEntry;

        internal HotUpdateStatus Status { get; private set; }
        internal bool IsInsideEntry => _isExecutingEntry && ReferenceEquals(_entryOwner.Value, this);

        internal HotUpdateStartup(FrameworkStartupConfig startup, HotUpdateStartupConfig configuration, ModuleManager host)
        {
            _startup = startup;
            _configuration = configuration;
            _host = host;
            if (startup.HotUpdateChannel != null)
                _releaseSelection = new HotUpdateReleaseSelection(startup, configuration, startup.HotUpdateChannel);
            SelectStage(HotUpdateStage.Configuration, configuration != null ? configuration.name : null);
        }

        #region 公共方法

        internal IAssetLoader CreateGatedLoader(string alias) => new GatedLoader(this, AssetModule.NormalizeAlias(alias));

        internal async Task<HotUpdateStatus> RunAsync(CancellationToken token)
        {
            if (_isClosing || Status.IsReady || Status.IsRestartRequired) return Status;
            // 入口失败后的注销和释放也必须在本发行所属的模块作用域内执行。
            using var scope = _host.EnterScope();
            try
            {
                await ExecuteAsync(token);
                token.ThrowIfCancellationRequested();
                if (_releaseSelection != null)
                    SelectStage(HotUpdateStage.ReleaseConfirmation, _plan.ReleaseId);

                HotUpdateStatus ready = CreateStatus(HotUpdateStage.Ready, null);
                // 核对整批页面工厂，再确认发行。确认成功后同步发布工厂并打开受管包。
                _releaseScope.Publish(() => _releaseSelection?.Confirm());
                Status = ready;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SelectStage(HotUpdateStage.Stopped, Status.Item);
            }
            catch (Exception exception)
            {
                Status = new HotUpdateStatus(Status.ReleaseId, Status.Stage, Status.Item, Status.ReleaseSource,
                    exception, _isRestartRequired, _releaseSelection?.FallbackReason);
            }
            finally
            {
                if (!Status.IsReady) CloseReleaseScope();
            }
            return Status;
        }

        internal void CloseGate() => _isClosing = true;

        internal void CloseReleaseScope()
        {
            HotUpdateReleaseScope scope = _releaseScope;
            _releaseScope = null;
            scope?.Dispose();
        }

        internal async Task ShutdownAsync()
        {
            CloseGate();
            CloseReleaseScope();
            try
            {
                var loaders = new List<IHotUpdateAssetLoader>(_loaders.Values);
                for (int i = loaders.Count - 1; i >= 0; i--)
                {
                    try { await loaders[i].ShutdownAsync(); }
                    catch (Exception exception) { Log.Error("[HotUpdate] 关闭受管资源包失败。", exception); }
                }
            }
            finally
            {
                _loaders.Clear();
                _releaseSelection?.Dispose();
                SelectStage(HotUpdateStage.Stopped, null);
            }
        }

        #endregion

        #region 私有逻辑

        private IHotUpdateAssetLoader RequireOpenPackage(string alias)
        {
            if (_isClosing || !Status.IsReady)
                throw new InvalidOperationException($"热更入口尚未就绪，资源包「{alias}」保持关闭（{Status.Stage}）。");
            return _loaders[alias];
        }

        private async Task ExecuteAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            SelectStage(HotUpdateStage.Configuration, _configuration != null ? _configuration.name : null);
            if (_configuration == null)
                throw new InvalidOperationException("启用发行渠道时须指定随包发行及其本地热更运行时适配器。");
            if (Volatile.Read(ref _hasProcessLoad) != 0)
            {
                _isRestartRequired = true;
                throw new InvalidOperationException("当前进程已执行热更装载，请在新的进程中启动发行。");
            }

            HotUpdatePlan plan = await SelectPlanAsync(token);
            IHotfixLoader runtime = plan.RuntimeProvider.CreateLoader() ??
                                    throw new InvalidOperationException("热更运行时适配器返回了空加载器。");
            if (Application.isEditor) ValidateEditorAssemblyNames(plan);
            _plan = plan;

            foreach (HotUpdatePlan.Package package in _plan.Packages)
            {
                token.ThrowIfCancellationRequested();
                SelectStage(HotUpdateStage.ResourceUpdate, $"{package.Alias}@{package.Version}");
                if (!_loaders.TryGetValue(package.Alias, out IHotUpdateAssetLoader loader))
                {
                    loader = package.Backend.CreateHotUpdateLoader() ??
                             throw new InvalidOperationException("资源包适配器返回了空加载器。");
                    _loaders.Add(package.Alias, loader);
                }
                await loader.PrepareVersionAsync(package.Version, package.RequiredAddresses, token);
            }

            // 先读取并核对所有字节，避免损坏的后续产物使进程只能带着部分装载结果退出。
            byte[][] metadata = await VerifyBinariesAsync(_plan.AotMetadata, token);
            byte[][] assemblies = await VerifyBinariesAsync(_plan.Assemblies, token);
            token.ThrowIfCancellationRequested();
            _isRestartRequired = true;
            if (Interlocked.CompareExchange(ref _hasProcessLoad, 1, 0) != 0)
                throw new InvalidOperationException("已有其他宿主执行进程级热更装载，请重启后重试。");

            _releaseScope = new HotUpdateReleaseScope();
            // 从装载开始暂存自动初始化触发的登记，直到入口结束。
            await _releaseScope.RunEntryAsync(() => LoadAndInvokeEntryAsync(runtime, metadata, assemblies, token));
        }

        private async Task LoadAndInvokeEntryAsync(IHotfixLoader runtime, byte[][] metadata, byte[][] assemblies,
            CancellationToken token)
        {
            for (int i = 0; i < metadata.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                HotUpdatePlan.Binary binary = _plan.AotMetadata[i];
                SelectStage(HotUpdateStage.AotMetadata, $"{binary.AssemblyName} · {binary.Key}");
                runtime.LoadMetadataForAOT(metadata[i]);
                metadata[i] = null;
            }

            Assembly entryAssembly = null;
            for (int i = 0; i < assemblies.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                HotUpdatePlan.Binary binary = _plan.Assemblies[i];
                SelectStage(HotUpdateStage.HotfixAssembly, $"{binary.AssemblyName} · {binary.Key}");
                Assembly loaded = runtime.LoadAssembly(assemblies[i]);
                assemblies[i] = null;
                if (loaded != null) UIPageFactories.RecordHotAssembly(loaded);
                string actualName = loaded?.GetName().Name;
                if (!string.Equals(binary.AssemblyName, actualName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"程序集名称不匹配。期望「{binary.AssemblyName}」，实际「{actualName}」。");
                if (binary.AssemblyName == _plan.EntryAssembly) entryAssembly = loaded;
            }

            token.ThrowIfCancellationRequested();
            SelectStage(HotUpdateStage.Entry, $"{_plan.EntryAssembly} · {_plan.EntryType}.{_plan.EntryMethod}");
            HotUpdateStartup previous = _entryOwner.Value;
            _entryOwner.Value = this;
            _isExecutingEntry = true;
            try
            {
                await InvokeEntryAsync(entryAssembly, token);
            }
            finally
            {
                _isExecutingEntry = false;
                _entryOwner.Value = previous;
            }
            token.ThrowIfCancellationRequested();
        }

        private async Task<HotUpdatePlan> SelectPlanAsync(CancellationToken token)
        {
            if (_releaseSelection == null)
            {
                RuntimePlatform platform = Application.isEditor ? _configuration.TargetPlatform : Application.platform;
                HotUpdatePlan plan = _plan ?? _configuration.BuildPlan(_startup, platform);
                plan.RuntimeProvider.ValidateRuntime(plan.AotMetadata.Count > 0);
                return plan;
            }

            SelectStage(HotUpdateStage.ReleaseSelection, _releaseSelection.ChannelId);
            // 选定后，本会话的重试继续使用同一计划。进入资源阶段前写入待确认记录。
            _plan ??= await _releaseSelection.SelectAsync(token);
            token.ThrowIfCancellationRequested();
            SelectStage(HotUpdateStage.ReleaseSelection, $"待确认发行「{_plan.ReleaseId}」");
            _releaseSelection.Begin();
            return _plan;
        }

        private async Task InvokeEntryAsync(Assembly assembly, CancellationToken token)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            token.ThrowIfCancellationRequested();

            string typeName = _plan.EntryType;
            string methodName = _plan.EntryMethod;
            Type type = assembly.GetType(typeName, throwOnError: false);
            if (type == null || !ReferenceEquals(type.Assembly, assembly))
                throw new TypeLoadException($"程序集 {assembly.GetName().Name} 中没有声明热更入口类型 {typeName}。");
            MethodInfo method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.ExactBinding,
                null, new[] { typeof(HotUpdateEntryContext), typeof(CancellationToken) }, null);

            // 固定签名确保协调器能等待入口结束，再确认整套发行。
            ParameterInfo[] parameters = method?.GetParameters();
            if (method == null || method.ContainsGenericParameters || method.ReturnType != typeof(Task) ||
                parameters.Length != 2 || parameters[0].ParameterType != typeof(HotUpdateEntryContext) ||
                parameters[1].ParameterType != typeof(CancellationToken))
                throw new MissingMethodException($"热更入口 {typeName}.{methodName} 必须声明为 public static Task 并接收 HotUpdateEntryContext 和 CancellationToken。");

            var context = new HotUpdateEntryContext(_plan.ReleaseId, _host, _releaseScope);
            Task operation;
            try
            {
                operation = (Task)method.Invoke(null, new object[] { context, token });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            if (operation == null)
                throw new InvalidOperationException($"热更入口 {typeName}.{methodName} 返回了空任务。");
            await operation;
            token.ThrowIfCancellationRequested();
        }

        private static void ValidateEditorAssemblyNames(HotUpdatePlan plan)
        {
            // 编辑器可能已从脚本目录编译同名程序集。IL2CPP 允许 HybridCLR 注册的占位程序集。
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                existing.Add(assembly.GetName().Name);
            foreach (HotUpdatePlan.Binary binary in plan.Assemblies)
                if (existing.Contains(binary.AssemblyName))
                    throw new InvalidOperationException(
                        $"编辑器已加载程序集「{binary.AssemblyName}」，请使用字节产物和独立运行环境验证。");
        }

        private async Task<byte[][]> VerifyBinariesAsync(IReadOnlyList<HotUpdatePlan.Binary> binaries, CancellationToken token)
        {
            var result = new byte[binaries.Count][];
            using var sha256 = SHA256.Create();
            for (int i = 0; i < binaries.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                HotUpdatePlan.Binary binary = binaries[i];
                SelectStage(HotUpdateStage.PayloadValidation, $"{binary.AssemblyName} · {binary.Key}");
                IHotUpdateAssetLoader loader = _loaders[binary.Alias];
                byte[] bytes;
                using (LoadedAsset loaded = await loader.LoadAsync(binary.Location))
                {
                    token.ThrowIfCancellationRequested();
                    bytes = (loaded?.Asset as TextAsset)?.bytes;
                }
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException($"「{binary.Key}」必须是非空的 TextAsset（.dll.bytes）。");
                string actualHash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty);
                if (!string.Equals(binary.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (loader is IHotUpdateAssetCacheRepair repair)
                    {
                        try { await repair.InvalidateCachedAssetAsync(binary.Location, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception exception) { Log.Warn($"「{binary.Key}」缓存修复失败。{exception.Message}"); }
                    }
                    throw new InvalidOperationException($"「{binary.Key}」SHA-256 不匹配。期望 {binary.Sha256}，实际 {actualHash}。");
                }
                result[i] = bytes;
            }
            return result;
        }

        private void SelectStage(HotUpdateStage stage, string item) => Status = CreateStatus(stage, item);

        private HotUpdateStatus CreateStatus(HotUpdateStage stage, string item)
        {
            HotUpdateReleaseSource source = _releaseSelection?.Source ??
                (_configuration != null ? HotUpdateReleaseSource.Bundled : HotUpdateReleaseSource.None);
            string releaseId = _plan?.ReleaseId;
            if (_releaseSelection == null && _configuration != null && releaseId == null)
                releaseId = _configuration.ReleaseId;
            return new HotUpdateStatus(releaseId, stage, item, source,
                fallbackReason: _plan != null ? _releaseSelection?.FallbackReason : null);
        }

        #endregion
    }
}
