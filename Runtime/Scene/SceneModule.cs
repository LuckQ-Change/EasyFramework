using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework
{
    /// <summary>串行提交场景切换、叠加加载和卸载，并管理每个实例的资源生命周期。</summary>
    public sealed class SceneModule : Module<SceneModule>
    {
        /// <summary>为提交中的操作保存进度映射并拦截同一调用链的重入。</summary>
        private sealed class Operation
        {
            /// <summary>将一个底层阶段的进度映射到整次操作。</summary>
            private sealed class StageProgress : IProgress<float>
            {
                private readonly Operation _operation;
                private readonly SceneOperationPhase _phase;
                private readonly float _begin;
                private readonly float _end;

                internal StageProgress(Operation operation, SceneOperationPhase phase, float begin, float end)
                {
                    _operation = operation;
                    _phase = phase;
                    _begin = begin;
                    _end = end;
                }

                /// <summary>在该阶段尚有效时公布进度。</summary>
                /// <param name="value">底层阶段的归一化进度。</param>
                public void Report(float value)
                {
                    if (!_operation.IsActive || _operation._phase != _phase || float.IsNaN(value)) return;
                    float progress = _begin + (_end - _begin) * Mathf.Clamp01(value);
                    if (progress > _operation._progress) _operation.Publish(_phase, progress);
                }
            }

            private readonly SceneModule _owner;
            private readonly SceneOperationKind _kind;
            private readonly string _message;
            private SceneOperationPhase _phase;
            private float _progress;
            private string _stageMessage;

            internal bool IsActive { get; set; }
            internal string Path { get; set; }

            internal Operation(SceneModule owner, SceneOperationKind kind, string path, string message)
            {
                _owner = owner;
                _kind = kind;
                Path = path;
                _message = message;
            }

            internal IProgress<float> BeginStage(SceneOperationPhase phase, float begin, float end, string message = null)
            {
                _stageMessage = message;
                Publish(phase, begin);
                return new StageProgress(this, phase, begin, end);
            }

            internal void Publish(SceneOperationPhase phase, float progress, Exception error = null)
            {
                _phase = phase;
                _progress = Math.Max(_progress, progress);
                _owner.Publish(new SceneOperationState(_kind, phase, Path, _progress, _stageMessage ?? _message, error));
            }
        }

        private static readonly AsyncLocal<Operation> _operationScope = new();
        private static SemaphoreSlim _sceneGate = new(1, 1);
        private readonly Dictionary<int, SceneHandle> _scenes = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly ISceneBackend _backend;
        private SemaphoreSlim _gate;
        private TaskCompletionSource<bool> _drained;
        private int _pending;
        private int _threadId;
        private bool _isStopping;
        private bool _isQuitting;
        private bool _hasAdoptedBootstrap;

        /// <summary>默认使用构建设置中的 Unity 场景。</summary>
        public SceneModule() : this(new UnitySceneBackend()) { }

        /// <summary>使用显式提供的场景适配器。</summary>
        /// <param name="backend">负责提交 Unity 场景操作的实现。</param>
        public SceneModule(ISceneBackend backend) => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        /// <summary>当前操作或最近一次操作的状态。</summary>
        public SceneOperationState State { get; private set; }
        /// <summary>此宿主最近激活的主场景。外部卸载后置空。</summary>
        public SceneHandle CurrentScene { get; private set; }
        /// <summary>在 Unity 主线程通知场景操作状态变化。</summary>
        public event Action<SceneOperationState> Changed;

        internal bool IsInsideOperation => _operationScope.Value?.IsActive == true;
        internal CancellationToken LifetimeToken => _shutdown.Token;

        #region 公共方法

        /// <summary>准备并激活主场景，再卸载上一个主场景。</summary>
        /// <param name="sceneNameOrPath">构建设置中的唯一场景名称或完整路径。</param>
        /// <param name="token">激活前可取消。进入提交时会完成旧场景清理。</param>
        /// <param name="prepare">首次加载后、激活前执行的可选准备回调。</param>
        /// <param name="message">加载展示文案。</param>
        /// <returns>切换成功后的主场景句柄。</returns>
        public Task<SceneHandle> SwitchAsync(string sceneNameOrPath, CancellationToken token = default,
            Func<SceneHandle, CancellationToken, Task> prepare = null, string message = null) =>
            ExecuteAsync(SceneOperationKind.Switch, sceneNameOrPath, message ?? "正在切换场景", token,
                (operation, cancellation) => LoadAsync(operation, cancellation, prepare, isSwitch: true));

        /// <summary>追加场景并持有其资源，当前主场景保持活动。</summary>
        /// <param name="sceneNameOrPath">构建设置中的唯一场景名称或完整路径。</param>
        /// <param name="token">取消追加并清理已经发出的加载。</param>
        /// <param name="prepare">首次加载后、发布句柄前的可选准备回调。</param>
        /// <param name="message">加载展示文案。</param>
        /// <returns>追加完成的场景句柄。</returns>
        public Task<SceneHandle> LoadAdditiveAsync(string sceneNameOrPath, CancellationToken token = default,
            Func<SceneHandle, CancellationToken, Task> prepare = null, string message = null) =>
            ExecuteAsync(SceneOperationKind.LoadAdditive, sceneNameOrPath, message ?? "正在加载场景", token,
                (operation, cancellation) => LoadAsync(operation, cancellation, prepare, isSwitch: false));

        /// <summary>按句柄卸载该宿主管理的场景实例。</summary>
        /// <param name="handle">待卸载的场景实例。</param>
        /// <param name="token">底层卸载提交前可取消。</param>
        /// <param name="message">卸载展示文案。</param>
        /// <returns>实例卸载、资源归还后的任务。</returns>
        public Task UnloadAsync(SceneHandle handle, CancellationToken token = default, string message = null)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!ReferenceEquals(handle.Owner, this))
                throw new ArgumentException("场景句柄属于其他宿主。", nameof(handle));
            return ExecuteAsync(SceneOperationKind.Unload, handle.Path, message ?? "正在卸载场景", token,
                async (operation, cancellation) =>
                {
                    if (!IsTracked(handle)) return true;
                    IProgress<float> progress = operation.BeginStage(SceneOperationPhase.Unloading, 0.05f, 0.98f);
                    cancellation.ThrowIfCancellationRequested();
                    await UnloadOwnedAsync(handle, progress);
                    return true;
                });
        }

        /// <summary>取得宿主现在跟踪的场景实例，包括需要重试清理的实例。</summary>
        /// <returns>当前仍加载的场景句柄快照。</returns>
        public IReadOnlyList<SceneHandle> GetLoadedScenes()
        {
            VerifyThread();
            var result = new List<SceneHandle>(_scenes.Count);
            foreach (SceneHandle handle in _scenes.Values)
                if (handle.IsLoaded) result.Add(handle);
            return result;
        }

        #endregion

        #region 生命周期

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneGate() => _sceneGate = new SemaphoreSlim(1, 1);

        protected override void OnInit()
        {
            _threadId = Environment.CurrentManagedThreadId;
            _gate = _sceneGate;
            _backend.SceneUnloaded += OnSceneUnloaded;
            Application.quitting += NotifyApplicationQuit;
            AdoptBootstrap();
        }

        protected override async Task OnShutdownAsync()
        {
            RequestStop();
            // Unity 已接纳的加载要先退出事务，关闭才能卸载剩余的自有场景。
            if (_pending > 0) await _drained.Task;
            if (_isQuitting || _gate == null) return;

            await WaitForGateAsync(CancellationToken.None);
            try
            {
                foreach (SceneHandle handle in new List<SceneHandle>(_scenes.Values))
                {
                    if (handle.IsBootstrap || !handle.IsLoaded) continue;
                    try
                    {
                        EnsureStandbyForLastScene();
                        await UnloadOwnedAsync(handle, null);
                    }
                    catch (Exception exception) { Log.Error($"[Scene] 关闭时无法卸载「{handle.Path}」。", exception); }
                }
            }
            finally { _gate.Release(); }
        }

        protected override void OnShutdown()
        {
            _backend.SceneUnloaded -= OnSceneUnloaded;
            Application.quitting -= NotifyApplicationQuit;
            foreach (SceneHandle handle in new List<SceneHandle>(_scenes.Values)) Forget(handle);
            CurrentScene = null;
            Changed = null;
            _shutdown.Dispose();
        }

        internal void NotifyApplicationQuit() => _isQuitting = true;

        internal void RequestStop()
        {
            if (_isStopping) return;
            _isStopping = true;
            try { _shutdown.Cancel(); }
            catch (Exception exception) { Log.Error("[Scene] 取消场景任务失败。", exception); }
        }

        #endregion

        #region 私有逻辑

        internal void VerifyThread()
        {
            if (_threadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("场景操作须由已启动的 Unity 主线程宿主发起。");
        }

        internal void VerifyResourceAccess()
        {
            VerifyThread();
            if (_isStopping) throw new ObjectDisposedException(nameof(SceneModule));
        }

        internal AssetScope CreateAssetScope() =>
            (Host.Get<AssetModule>() ?? throw new InvalidOperationException("场景持有资源需要装配 AssetModule。")).CreateScope();

        private async Task<T> ExecuteAsync<T>(SceneOperationKind kind, string path, string message,
            CancellationToken token, Func<Operation, CancellationToken, Task<T>> action)
        {
            VerifyThread();
            if (_isStopping) throw new ObjectDisposedException(nameof(SceneModule));
            if (IsInsideOperation) throw new InvalidOperationException("请在当前场景操作结束后再发起新的场景操作。");
            token.ThrowIfCancellationRequested();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, token);
            if (_pending++ == 0)
                _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await WaitForGateAsync(cancellation.Token);
                Operation previous = _operationScope.Value;
                var operation = new Operation(this, kind, path, message) { IsActive = true };
                _operationScope.Value = operation;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    using var scope = Host.EnterScope();
                    operation.Publish(SceneOperationPhase.Preparing, 0f);
                    T result = await action(operation, cancellation.Token);
                    operation.Publish(SceneOperationPhase.Completed, 1f);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    operation.Publish(SceneOperationPhase.Canceled, 0f);
                    throw;
                }
                catch (Exception exception)
                {
                    operation.Publish(SceneOperationPhase.Failed, 0f, exception);
                    throw;
                }
                finally
                {
                    operation.IsActive = false;
                    _operationScope.Value = previous;
                    _gate.Release();
                }
            }
            finally
            {
                if (--_pending == 0) _drained.TrySetResult(true);
            }
        }

        private async Task<SceneHandle> LoadAsync(Operation operation, CancellationToken token,
            Func<SceneHandle, CancellationToken, Task> prepare, bool isSwitch)
        {
            string path = operation.Path = _backend.ResolvePath(operation.Path);
            AdoptBootstrap();
            Scene beforeLoad = _backend.ActiveScene;
            SceneHandle previous = CurrentScene;
            SceneHandle candidate = null;
            bool isNew = false;
            bool hasCommitted = false;
            try
            {
                if (_backend.TryGetLoadedScene(path, out Scene loaded))
                {
                    if (!_scenes.TryGetValue(loaded.handle, out candidate))
                        throw new InvalidOperationException($"场景「{path}」已由其他入口加载。");
                    if (!candidate.IsReady)
                        throw new InvalidOperationException($"场景「{path}」仍在等待清理，请先按现有句柄卸载。");
                }
                else
                {
                    IProgress<float> progress = operation.BeginStage(SceneOperationPhase.Loading, 0.05f, 0.72f);
                    token.ThrowIfCancellationRequested();
                    loaded = await _backend.LoadAdditiveAsync(path, progress);
                    candidate = Track(loaded, path, isBootstrap: false, preparationToken: token);
                    isNew = true;
                    token.ThrowIfCancellationRequested();
                    operation.Publish(SceneOperationPhase.Preparing, 0.78f);
                    token.ThrowIfCancellationRequested();
                    if (prepare != null) await prepare(candidate, token);
                }

                if (isSwitch) operation.Publish(SceneOperationPhase.Activating, 0.86f);
                token.ThrowIfCancellationRequested();
                if (!candidate.IsLoaded) throw new InvalidOperationException($"场景「{path}」已在提交前被卸载。");
                if (isSwitch)
                {
                    if (!_backend.SetActiveScene(candidate.Scene))
                        throw new InvalidOperationException($"无法激活场景「{path}」。");
                    CurrentScene = candidate;
                }
                candidate.MarkReady();
                hasCommitted = true;

                if (isSwitch && previous != null && previous != candidate)
                {
                    IProgress<float> progress = operation.BeginStage(SceneOperationPhase.Unloading, 0.9f, 0.99f,
                        "正在释放上一个场景");
                    try { await UnloadOwnedAsync(previous, progress); }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"「{path}」已激活，旧场景「{previous.Path}」卸载失败。可按旧句柄重试卸载。", exception);
                    }
                }
                return candidate;
            }
            catch (Exception original)
            {
                if (!hasCommitted && isNew && IsTracked(candidate))
                {
                    candidate.EndPreparation();
                    try
                    {
                        IProgress<float> cleanup = operation.BeginStage(SceneOperationPhase.CleaningUp, 0.9f, 0.99f,
                            "正在清理未提交的场景");
                        if (_backend.ActiveScene.handle == candidate.Scene.handle && IsLoaded(beforeLoad))
                            _backend.SetActiveScene(beforeLoad);
                        if (candidate.IsLoaded) EnsureStandbyForLastScene();
                        await UnloadOwnedAsync(candidate, cleanup);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException($"作废场景「{path}」清理失败。可按场景句柄重试卸载。",
                            new AggregateException(original, exception));
                    }
                }
                throw;
            }
        }

        private async Task UnloadOwnedAsync(SceneHandle handle, IProgress<float> progress)
        {
            if (!IsTracked(handle)) return;
            if (!handle.IsLoaded)
            {
                Forget(handle);
                return;
            }
            if (_backend.LoadedSceneCount <= 1)
                throw new InvalidOperationException("当前只有一个已加载场景。请先切换或叠加加载另一个场景。");

            bool wasPrimary = ReferenceEquals(CurrentScene, handle);
            try
            {
                await _backend.UnloadAsync(handle.Scene, progress);
                if (IsLoaded(handle.Scene))
                    throw new InvalidOperationException($"适配器结束卸载后，场景「{handle.Path}」仍处于加载状态。");
            }
            finally
            {
                if (!handle.IsLoaded) Forget(handle);
                if (wasPrimary && CurrentScene == null && _scenes.TryGetValue(_backend.ActiveScene.handle, out var current))
                    CurrentScene = current;
            }
        }

        private void EnsureStandbyForLastScene()
        {
            if (_backend.LoadedSceneCount != 1) return;
            Scene standby = _backend.CreateStandbyScene();
            if (!_backend.SetActiveScene(standby))
                throw new InvalidOperationException("无法激活用于释放最后一个场景的待机场景。");
        }

        private void AdoptBootstrap()
        {
            if (_hasAdoptedBootstrap || !ReferenceEquals(Host, ModuleManager.Default)) return;
            Scene active = _backend.ActiveScene;
            if (!active.IsValid()) return;
            _hasAdoptedBootstrap = true;
            CurrentScene = Track(active, string.IsNullOrEmpty(active.path) ? active.name : active.path, isBootstrap: true);
        }

        private SceneHandle Track(Scene scene, string path, bool isBootstrap, CancellationToken preparationToken = default)
        {
            if (!scene.IsValid() || (!isBootstrap && !scene.isLoaded))
                throw new InvalidOperationException($"适配器没有返回可用的场景「{path}」。");
            var handle = new SceneHandle(this, scene, path, isBootstrap, preparationToken);
            _scenes.Add(scene.handle, handle);
            return handle;
        }

        private bool IsTracked(SceneHandle handle) =>
            handle != null && _scenes.TryGetValue(handle.Scene.handle, out var current) && ReferenceEquals(handle, current);

        private static bool IsLoaded(Scene scene) => scene.IsValid() && scene.isLoaded;

        private void OnSceneUnloaded(Scene scene)
        {
            if (_scenes.TryGetValue(scene.handle, out SceneHandle handle)) Forget(handle);
        }

        private void Forget(SceneHandle handle)
        {
            if (!IsTracked(handle)) return;
            _scenes.Remove(handle.Scene.handle);
            if (ReferenceEquals(CurrentScene, handle)) CurrentScene = null;
            try { handle.Release(); }
            catch (Exception exception) { Log.Error($"[Scene] 归还「{handle.Path}」的资源失败。", exception); }
        }

        private void Publish(SceneOperationState state)
        {
            State = state;
            Action<SceneOperationState> handlers = Changed;
            if (handlers == null) return;
            foreach (Action<SceneOperationState> handler in handlers.GetInvocationList())
            {
                try { handler(state); }
                catch (Exception exception) { Log.Error("[Scene] 状态监听回调失败。", exception); }
            }
        }

        private async Task WaitForGateAsync(CancellationToken token)
        {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
            // WebGL 没有可依赖的托管线程池，等待逻辑继续在 Unity 主线程推进。
            while (!_gate.Wait(0))
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
#else
            await _gate.WaitAsync(token);
#endif
        }

        #endregion
    }
}
