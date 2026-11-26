using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework
{
    /// <summary>绑定某个已加载场景实例和它所持有资源的句柄。</summary>
    public sealed class SceneHandle
    {
        private AssetScope _assets;
        private CancellationToken _preparationToken;
        private bool _isReleased;

        /// <summary>底层 Unity 场景实例。</summary>
        public Scene Scene { get; }
        /// <summary>加载时锁定的场景路径，卸载后仍可用于诊断。</summary>
        public string Path { get; }
        /// <summary>该实例是否仍加载且由宿主管理。</summary>
        public bool IsLoaded => !_isReleased && Scene.IsValid() && Scene.isLoaded;
        /// <summary>是否已通过准备，可以复用或接管主场景。</summary>
        public bool IsReady { get; private set; }

        internal SceneModule Owner { get; }
        internal bool IsBootstrap { get; }

        internal SceneHandle(SceneModule owner, Scene scene, string path, bool isBootstrap, CancellationToken preparationToken)
        {
            Owner = owner;
            Scene = scene;
            Path = path;
            IsBootstrap = isBootstrap;
            IsReady = isBootstrap;
            _preparationToken = preparationToken;
        }

        /// <summary>加载并持有一个场景资源，卸载此实例时归还。</summary>
        /// <typeparam name="T">待获取的 Unity 资源类型。</typeparam>
        /// <param name="address">AssetModule 能解析的资源地址。</param>
        /// <param name="token">用于取消本次资源获取的令牌。</param>
        /// <returns>场景作用域持有的资源。</returns>
        public async Task<T> LoadAssetAsync<T>(string address, CancellationToken token = default) where T : UnityEngine.Object
        {
            Owner.VerifyResourceAccess();
            token.ThrowIfCancellationRequested();
            if (!IsLoaded) throw new ObjectDisposedException(nameof(SceneHandle), $"场景「{Path}」已释放。");
            if (!IsReady && !_preparationToken.CanBeCanceled)
                throw new InvalidOperationException($"场景「{Path}」尚未就绪，正在等待清理。");

            // 准备中的资源等待随本次切换取消，已就绪的资源等待随宿主关闭取消。
            CancellationToken ownerToken = IsReady ? Owner.LifetimeToken : _preparationToken;
            using var cancellation = token.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ownerToken, token)
                : null;
            return await (_assets ??= Owner.CreateAssetScope()).LoadAsync<T>(address, cancellation?.Token ?? ownerToken);
        }

        internal void MarkReady()
        {
            IsReady = true;
            EndPreparation();
        }

        internal void EndPreparation() => _preparationToken = default;

        internal void Release()
        {
            if (_isReleased) return;
            _isReleased = true;
            IsReady = false;
            EndPreparation();
            AssetScope assets = _assets;
            _assets = null;
            assets?.Dispose();
        }
    }
}
