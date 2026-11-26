using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.Tweening;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    /// <summary>控制可选的运行时诊断功能是否在开发环境启用。</summary>
    public enum DebugWindowMode
    {
        [InspectorName("开发构建")]
        DevelopmentBuild = 0,
        [InspectorName("关闭")]
        Disabled = 2,
    }

    /// <summary>启动配置可选的内置模块集合。</summary>
    [Flags]
    public enum FrameworkBuiltinModules
    {
        None = 0,
        Event = 1 << 0,
        Timer = 1 << 1,
        Pool = 1 << 2,
        Tween = 1 << 3,
        Audio = 1 << 4,
        Default = Event | Timer | Pool | Tween | Audio,
    }

    /// <summary>项目可派生装配器资产，为指定启动配置声明模块工厂和依赖。</summary>
    public abstract class FrameworkModuleInstaller : ScriptableObject
    {
        /// <summary>向宿主的模块图追加声明。实际模块会在完整依赖图校验通过后创建。</summary>
        /// <param name="composition">当前宿主独享的装配配置。</param>
        public abstract void Configure(ModuleComposition composition);
    }

    /// <summary>保存模块装配、资源和显示基础设施配置。</summary>
    [CreateAssetMenu(
        fileName = "FrameworkStartupConfig",
        menuName = "EasyFramework/Startup Config",
        order = 1)]
    public sealed class FrameworkStartupConfig : ScriptableObject
    {
        internal const string OFFLINE_CHANNEL_ERROR =
            "离线模式（Offline）不能配置远程发行渠道。请清空「发行渠道」（Hot Update Channel），可保留随包发行。";

        [Header("模块装配")]
        [SerializeField] private FrameworkBuiltinModules _builtinModules = FrameworkBuiltinModules.Default;
        [SerializeField] private bool _isSceneModuleEnabled = true;
        [SerializeField] private FrameworkModuleInstaller[] _moduleInstallers = Array.Empty<FrameworkModuleInstaller>();

        [Header("UI 启动")]
        [SerializeField] private bool _startUI = true;
        [SerializeField] private UIPageManifest _pageManifest;
        [SerializeField] private UIRenderMode _renderMode = UIRenderMode.ScreenSpaceCamera;
        [SerializeField] private Camera _uiCamera;
        [SerializeField] private bool _createCameraWhenMissing = true;
        [SerializeField] private bool _createEventSystem = true;

        [Header("UI 相机")]
        [SerializeField] private string _uiLayerName = "UI";
        [SerializeField] private float _cameraDepth = 100f;
        [SerializeField, Min(0.01f)] private float _planeDistance = 100f;

        [Header("UI 屏幕适配")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;

        [Header("UI 共享背景")]
        [SerializeField] private GameObject _backgroundPrefab;

        [Header("运行环境")]
        [SerializeField] private RuntimeMode _runtimeMode = RuntimeMode.Editor;

        [Header("资源包与空闲缓存")]
        [SerializeField] private AssetPackageConfig[] _assetPackages = Array.Empty<AssetPackageConfig>();
        [SerializeField] private string[] _residentAssets = Array.Empty<string>();
        [SerializeField, Min(1f)] private float _startupAssetTimeout = 15f;
        [SerializeField] private bool _overrideAssetCache;
        [SerializeField] private AssetCacheSettings _assetCache = new AssetCacheSettings();

        [Header("可选热更启动")]
        [SerializeField] private HotUpdateStartupConfig _hotUpdate;
        [SerializeField] private HotUpdateChannelConfig _hotUpdateChannel;
        [SerializeField] private string _playerBaselineId;

        [Header("可选运行时诊断")]
        [Tooltip("装配诊断程序集时，可在编辑器 Play Mode 和 Development Build 中开启。")]
        [SerializeField] private DebugWindowMode _debugWindowMode = DebugWindowMode.Disabled;

        #region 公共方法

        /// <summary>默认宿主是否创建 UI 画布和事件系统。</summary>
        public bool StartUI => _startUI;
        /// <summary>默认 UI 宿主打开托管页面时使用的页面清单。</summary>
        public UIPageManifest PageManifest => _pageManifest;
        /// <summary>UI 画布的渲染方式。</summary>
        public UIRenderMode RenderMode => _renderMode;
        /// <summary>可选的显式 UI 相机引用。</summary>
        public Camera UICamera => _uiCamera;
        /// <summary>缺少显式相机时是否创建 UI 相机。</summary>
        public bool CreateCameraWhenMissing => _createCameraWhenMissing;
        /// <summary>是否创建缺少的全局事件系统。</summary>
        public bool CreateEventSystem => _createEventSystem;
        /// <summary>UI 使用的 Unity 层名称。</summary>
        public string UILayerName => _uiLayerName;
        /// <summary>UI 相机深度。</summary>
        public float CameraDepth => _cameraDepth;
        /// <summary>相机 UI 平面与相机的距离。</summary>
        public float PlaneDistance => _planeDistance;
        /// <summary>UI 画布使用的参考分辨率。</summary>
        public Vector2 ReferenceResolution => _referenceResolution;
        /// <summary>画布缩放的宽高匹配权重。</summary>
        public float MatchWidthOrHeight => _matchWidthOrHeight;
        /// <summary>UI 的共享背景模板。</summary>
        public GameObject BackgroundPrefab => _backgroundPrefab;
        /// <summary>启动时为该宿主选择的运行模式。</summary>
        public RuntimeMode RuntimeMode => _runtimeMode;
        /// <summary>启动配置声明的资源包。</summary>
        public IReadOnlyList<AssetPackageConfig> AssetPackages => _assetPackages ?? Array.Empty<AssetPackageConfig>();
        /// <summary>本地 Player 归档所对应的基线 ID。</summary>
        public string PlayerBaselineId => _playerBaselineId?.Trim();
        /// <summary>随 Player 归档的发行和本地运行时适配配置。</summary>
        public HotUpdateStartupConfig HotUpdate => _hotUpdate;
        /// <summary>可选的远程发行渠道。留空时使用随包发行。</summary>
        public HotUpdateChannelConfig HotUpdateChannel => _hotUpdateChannel;
        /// <summary>项目中保存的诊断启用方式。</summary>
        public DebugWindowMode ConfiguredDebugWindowMode => _debugWindowMode;

        /// <summary>当前开发环境是否请求启动可选诊断。</summary>
        public bool ShowDebugWindow => Debug.isDebugBuild && _debugWindowMode == DebugWindowMode.DevelopmentBuild;
        /// <summary>按照当前配置创建或更新持久化 UI 根节点。</summary>
        /// <returns>启用了配置中的 UI 时返回显示宿主。</returns>
        public UIRuntimeHost Apply() => Apply(out _);

        #endregion

        #region 私有逻辑

        internal UIRuntimeHost Apply(out UIRuntimeHost ownedUI)
        {
            ownedUI = null;
            ModuleManager owner = ModuleManager.Current;
            if (owner != null && !ReferenceEquals(owner, ModuleManager.Default))
                throw new InvalidOperationException("隔离宿主不能配置默认全局 UI。");
            if (_pageManifest != null) _pageManifest.Validate();

            UIRuntimeHost host = null;
            if (_startUI)
            {
                UIRuntimeHost previous = UIManager.Current?.RuntimeHost;
                if (previous == null) previous = UnityEngine.Object.FindObjectOfType<UIRuntimeHost>(true);
                UIManager manager = UIManager.Instance;
                if (manager != null)
                {
                    host = manager.RuntimeHost;
                    if (previous == null) ownedUI = host;
                    manager.SetPageManifest(_pageManifest);
                    host.Configure(
                        _renderMode,
                        _uiCamera,
                        _createCameraWhenMissing,
                        _createEventSystem,
                        _uiLayerName,
                        _cameraDepth,
                        _planeDistance,
                        _referenceResolution,
                        _matchWidthOrHeight,
                        _backgroundPrefab);
                }
            }
            else UIManager.Current?.SetPageManifest(_pageManifest);

            return host;
        }

        internal IReadOnlyList<string> ResidentAssets => _residentAssets ?? Array.Empty<string>();

        internal float StartupAssetTimeout => _startupAssetTimeout >= 1f && !float.IsInfinity(_startupAssetTimeout)
            ? _startupAssetTimeout : 15f;

        private bool HasAssetModuleConfiguration => AssetPackages.Count > 0 || ResidentAssets.Count > 0 || _overrideAssetCache;

        internal void ValidateStartupMode()
        {
            if (_runtimeMode == RuntimeMode.Offline && _hotUpdateChannel != null)
                throw new InvalidOperationException(OFFLINE_CHANNEL_ERROR);
        }

        internal void AppendModules(ModuleComposition composition, HotUpdateStartup hotUpdate)
        {
            if (HasAssetModuleConfiguration) composition.Add<AssetModule>(_ => CreateAssetModule(hotUpdate));
            if ((_builtinModules & FrameworkBuiltinModules.Event) != 0) composition.Add<EventModule>();
            if ((_builtinModules & FrameworkBuiltinModules.Timer) != 0) composition.Add<TimerModule>();
            if ((_builtinModules & FrameworkBuiltinModules.Pool) != 0) composition.Add<PoolModule>();
            if ((_builtinModules & FrameworkBuiltinModules.Tween) != 0) composition.Add<TweenModule>();
            if ((_builtinModules & FrameworkBuiltinModules.Audio) != 0) composition.Add<AudioModule>();
            if (_moduleInstallers == null) return;
            foreach (FrameworkModuleInstaller installer in _moduleInstallers)
            {
                if (installer == null) throw new InvalidOperationException("启动配置包含空的模块装配器。");
                installer.Configure(composition);
            }
        }

        internal void AppendSceneModule(ModuleComposition composition)
        {
            if (!_isSceneModuleEnabled) return;
            bool hasAssets = Array.Exists(composition.CopyRegistrations(), registration => registration.Type == typeof(AssetModule));
            if (hasAssets) composition.Add<SceneModule>(typeof(AssetModule));
            else composition.Add<SceneModule>();
        }

        internal async Task WarmLocalPackagesAsync(AssetModule assets, CancellationToken token)
        {
            if (assets == null) return;
            foreach (AssetPackageConfig package in AssetPackages)
                if (package is ReferencedAssetPackageConfig) await assets.WarmPackageAsync(package.Alias, token);
        }

        internal bool HasHotUpdateResidentAssets()
        {
            foreach (AssetPackageConfig package in AssetPackages)
            {
                if (package == null || !(package is IHotUpdatePackageConfig)) continue;
                string managedAlias = package.Alias?.Trim();
                if (string.IsNullOrEmpty(managedAlias) || managedAlias.IndexOf(':') >= 0) continue;
                foreach (string address in ResidentAssets)
                {
                    int delimiter = address?.IndexOf(':') ?? -1;
                    string alias = delimiter < 0 ? AssetModule.MainPackage : address.Substring(0, delimiter).Trim();
                    if (alias == managedAlias) return true;
                }
            }
            return false;
        }

        private AssetModule CreateAssetModule(HotUpdateStartup hotUpdate)
        {
            var assets = new AssetModule();
            ConfigureAssets(assets, hotUpdate);
            return assets;
        }

        private void ConfigureAssets(AssetModule assets, HotUpdateStartup hotUpdate)
        {
            if (_overrideAssetCache) assets.ConfigureCache(_assetCache ?? new AssetCacheSettings());
            foreach (AssetPackageConfig package in AssetPackages)
            {
                if (package == null)
                {
                    // 缺少热更适配资产的错误由热更配置阶段报告，保留本地启动入口。
                    if (hotUpdate != null) continue;
                    throw new InvalidOperationException("启动配置包含空的资源包。");
                }
                if (hotUpdate == null || !(package is IHotUpdatePackageConfig))
                    assets.RegisterPackage(package.Alias, package.CreateLoader);
            }

            if (hotUpdate == null) return;
            foreach (AssetPackageConfig package in AssetPackages)
            {
                if (package == null || !(package is IHotUpdatePackageConfig)) continue;
                string alias = package.Alias?.Trim();
                // 无效或冲突的受管包留给热更预检报告，本地资源包仍然可用。
                if (string.IsNullOrEmpty(alias) || alias.IndexOf(':') >= 0 || assets.IsPackageRegistered(alias)) continue;
                assets.RegisterPackage(alias, () => hotUpdate.CreateGatedLoader(alias));
            }
        }

        #endregion

        #region 生命周期

        private void OnValidate()
        {
            if (_referenceResolution.x < 1f) _referenceResolution.x = 1f;
            if (_referenceResolution.y < 1f) _referenceResolution.y = 1f;
            if (_planeDistance < 0.01f) _planeDistance = 0.01f;
            if (string.IsNullOrWhiteSpace(_uiLayerName)) _uiLayerName = "UI";
        }

        #endregion
    }
}
