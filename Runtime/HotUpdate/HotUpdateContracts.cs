using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine.Scripting;

namespace EasyFramework
{
    /// <summary>资源包配置可以为热更启动创建独立、锁定版本的后端。</summary>
    public interface IHotUpdatePackageConfig
    {
        /// <summary>检查指定运行模式所需的包配置。</summary>
        /// <param name="mode">当前宿主配置的运行模式。</param>
        /// <param name="version">发行指定的包清单版本。</param>
        void ValidateHotUpdateConfiguration(RuntimeMode mode, string version);

        /// <summary>创建由本次热更启动独占的包后端。</summary>
        /// <returns>尚未准备发行版本的资源包后端。</returns>
        IHotUpdateAssetLoader CreateHotUpdateLoader();
    }

    /// <summary>支持显式版本准备的资源包后端。</summary>
    public interface IHotUpdateAssetLoader : IAssetLoader
    {
        /// <summary>准备指定版本的清单和启动所需资源，完成后可读取该版本。</summary>
        /// <param name="version">发行配置锁定的包版本。</param>
        /// <param name="requiredAddresses">当前包中需要预下载的地址，不含包别名。</param>
        /// <param name="token">取消本次准备的令牌。</param>
        /// <returns>本次准备结束的任务，同版本重试可复用已完成的步骤。</returns>
        Task PrepareVersionAsync(string version, IReadOnlyList<string> requiredAddresses, CancellationToken token);
    }

    /// <summary>允许后端定点修复摘要校验失败的本地缓存。</summary>
    public interface IHotUpdateAssetCacheRepair
    {
        /// <summary>在调用方已释放资源句柄后，使关联的缓存内容重新下载。</summary>
        /// <param name="address">完整性校验失败的包内地址。</param>
        /// <param name="token">取消缓存清理的令牌。</param>
        /// <returns>缓存清理结束的任务。</returns>
        Task InvalidateCachedAssetAsync(string address, CancellationToken token);
    }

    /// <summary>可选热更组合当前执行的阶段。</summary>
    public enum HotUpdateStage
    {
        Configuration,
        ResourceUpdate,
        PayloadValidation,
        AotMetadata,
        HotfixAssembly,
        Entry,
        Ready,
        Stopped,
        ReleaseSelection,
        ReleaseConfirmation,
    }

    /// <summary>本次启动选定的整份发行来自哪里。</summary>
    public enum HotUpdateReleaseSource
    {
        None,
        Bundled,
        Channel,
        Confirmed,
    }

    /// <summary>热更启动的只读阶段或失败快照。</summary>
    public sealed class HotUpdateStatus
    {
        /// <summary>本次启动所选的发行 ID。</summary>
        public string ReleaseId { get; }
        /// <summary>本次锁定发行的来源。</summary>
        public HotUpdateReleaseSource ReleaseSource { get; }
        /// <summary>正在执行或发生失败的阶段。</summary>
        public HotUpdateStage Stage { get; }
        /// <summary>阶段涉及的配置项、包版本、字节地址或入口。</summary>
        public string Item { get; }
        /// <summary>触发发行回退的实际原因。未尝试回退时为空。</summary>
        public string FallbackReason { get; }
        /// <summary>失败时记录的原始错误。</summary>
        public Exception Error { get; }
        /// <summary>是否已进入不可撤回的进程级装载，需要退出进程后重试。</summary>
        public bool IsRestartRequired { get; }
        /// <summary>是否允许在当前会话重试同一发行。</summary>
        public bool CanRetry => Error != null && !IsRestartRequired;
        /// <summary>入口与所有前置阶段是否都已成功。</summary>
        public bool IsReady => Stage == HotUpdateStage.Ready;

        internal HotUpdateStatus(string releaseId, HotUpdateStage stage, string item,
            HotUpdateReleaseSource releaseSource, Exception error = null, bool isRestartRequired = false,
            string fallbackReason = null)
        {
            ReleaseId = releaseId;
            ReleaseSource = releaseSource;
            FallbackReason = fallbackReason;
            Stage = stage;
            Item = item;
            Error = error;
            IsRestartRequired = isRestartRequired;
        }
    }

    /// <summary>固定热更入口接收的 AOT 侧上下文。</summary>
    public sealed class HotUpdateEntryContext
    {
        private readonly HotUpdateReleaseScope _scope;

        /// <summary>成功准备并正在启动的发行 ID。</summary>
        public string ReleaseId { get; }
        /// <summary>执行本次热更入口的模块宿主。</summary>
        public ModuleManager Modules { get; }

        internal HotUpdateEntryContext(string releaseId, ModuleManager modules, HotUpdateReleaseScope scope)
        {
            ReleaseId = releaseId;
            Modules = modules;
            _scope = scope;
        }

        /// <summary>在 Unity 主线程登记页面工厂，发行成功后统一发布。</summary>
        /// <typeparam name="T">此工厂创建的具体页面类型。</typeparam>
        /// <param name="factoryKey">页面清单使用的工厂键。</param>
        /// <param name="factory">每次创建一个新 View 的函数。</param>
        [Preserve]
        public void RegisterPageFactory<T>(string factoryKey, Func<T> factory) where T : UIView =>
            _scope.RegisterPageFactory(factoryKey, factory);

        /// <summary>在 Unity 主线程将订阅句柄、资源租约或作用域交给发行管理。</summary>
        /// <typeparam name="T">可释放句柄的类型。</typeparam>
        /// <param name="handle">发行失败或关闭时需要释放的句柄。</param>
        /// <returns>由发行持有的同一个句柄。</returns>
        [Preserve]
        public T Own<T>(T handle) where T : class, IDisposable => _scope.Own(handle);

        /// <summary>在 Unity 主线程登记发行失败或关闭时执行的清理回调。</summary>
        /// <param name="cleanup">用于解除事件订阅或还原项目自有状态的回调。</param>
        [Preserve]
        public void OnDispose(Action cleanup) => _scope.OnDispose(cleanup);
    }
}
