using System;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>通用 Prefab 显示组件，负责创建记录的纯 C# View/Item 与 Binding 对象。</summary>
    [AddComponentMenu("EasyFramework/UI/UI Display")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class UIDisplay : MonoBehaviour
    {
        [Header("View 脚本")]
        [SerializeField] private string _viewTypeName;
        [SerializeField] private string _viewScriptPath;
        [SerializeField] private string _prefabLocation;
        [SerializeField] private bool _createOnAwake = true;

        [Header("View 配置")]
        [SerializeField] private bool _managedAsView = true;
        [SerializeField] private string _viewId;
        [SerializeField] private UILayer _defaultLayer = UILayer.Screen;
        [SerializeField] private bool _singleInstance = true;
        [SerializeField] private bool _closeOnBack = true;
        [SerializeField] private bool _stretchToLayer = true;

        [Header("共享背景")]
        [SerializeField] private UIBackgroundMode _backgroundMode;
        [SerializeField] private bool _closeOnBackground;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        private UIObject _logic;
        private UIBinding _binding;
        private UIBindingContext _bindingContext;
        private bool _created;
        private bool _focused;

        public string ViewTypeName => _viewTypeName;
        public string ViewScriptPath => _viewScriptPath;
        /// <summary>
        /// View 脚本上的 UIPrefab 是资源地址的权威来源；序列化字段仅为脚本尚未编译时的回退缓存。
        /// </summary>
        public string PrefabLocation
        {
            get
            {
                Type viewType = UIFactory.ResolveType(_viewTypeName);
                return UIFactory.TryGetPrefabLocation(viewType, out string location)
                    ? location
                    : _prefabLocation;
            }
        }
        public bool ManagedAsView => _managedAsView;
        public string ViewId => _viewId;
        public UILayer DefaultLayer => _defaultLayer;
        public UILayer CurrentLayer { get; internal set; }
        public bool SingleInstance => _singleInstance;
        public bool CloseOnBack => _closeOnBack;
        public bool StretchToLayer => _stretchToLayer;
        public UIBackgroundMode BackgroundMode => _backgroundMode;
        public bool UsesBackground => _backgroundMode != UIBackgroundMode.None;
        public bool CloseOnBackground => _closeOnBackground;
        public Color BackgroundColor => _backgroundColor;
        public bool IsOpen { get; private set; }
        public bool IsVisible => IsOpen && gameObject.activeSelf;
        public bool IsFocused => _focused;
        public object OpenArgs { get; private set; }
        public UIManager Manager { get; private set; }
        public UIObject Logic => EnsureCreated();
        public UIView View => EnsureCreated() as UIView;
        public UIBinding Binding
        {
            get
            {
                EnsureCreated();
                return _binding;
            }
        }
        public UIBindingContext BindingContext =>
            _bindingContext == null ? (_bindingContext = GetComponent<UIBindingContext>()) : _bindingContext;

        public event Action<UIDisplay, object> Opened;
        public event Action<UIDisplay> Closed;

        private void Awake()
        {
            if (_createOnAwake) EnsureCreated();
        }

        public T GetLogic<T>() where T : UIObject => EnsureCreated() as T;
        public void Close() => Manager?.Close(this);
        public void BringToFront() => Manager?.BringToFront(this);
        public void SetLayer(UILayer layer) => Manager?.SetLayer(this, layer);

        public UIObject EnsureCreated()
        {
            if (_logic != null) return _logic;
            if (!UIFactory.TryCreate(_viewTypeName, out _logic, out _binding))
            {
                if (!string.IsNullOrWhiteSpace(_viewTypeName))
                    throw new InvalidOperationException(
                        $"{name}：无法创建 UI 逻辑“{_viewTypeName}”。" +
                        "请确认 Prefab 保存的完整类型名与脚本命名空间一致，且脚本已成功编译。");
                _binding = new UIBinding();
                _logic = _managedAsView ? (UIObject)new UIView() : new UIItem();
            }

            try
            {
                _binding.Initialize(this);
                _logic.Initialize(this, _binding);
                // 受管理的 View 持有自己的绑定源；默认绑定源就是 View 本身，
                // 有特殊需求时可重写 BindingSource。
                if (_managedAsView && BindingContext != null)
                    BindingContext.SetSource(_logic.BindingSource);
                return _logic;
            }
            catch
            {
                _logic?.Dispose();
                _binding?.Dispose();
                _logic = null;
                _binding = null;
                throw;
            }
        }

        public void SetViewScript(
            string viewTypeName,
            string viewScriptPath,
            string prefabLocation)
        {
            _viewTypeName = viewTypeName;
            _viewScriptPath = viewScriptPath;
            _prefabLocation = prefabLocation;
        }

        internal void Initialize(UIManager manager, UILayer layer)
        {
            Manager = manager;
            CurrentLayer = layer;
            if (_created) return;
            _created = true;
            UIObject logic = EnsureCreated();
            if (_managedAsView && !(logic is UIView))
                throw new InvalidOperationException(
                    $"{name} is managed as a view, but {logic.GetType().Name} does not inherit UIView.");
        }

        internal void OpenInternal(object args)
        {
            OpenArgs = args;
            IsOpen = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            View?.Open(args);
            Opened?.Invoke(this, args);
        }

        internal void FocusInternal()
        {
            if (_focused) return;
            _focused = true;
            View?.Focus();
        }

        internal void BlurInternal()
        {
            if (!_focused) return;
            _focused = false;
            View?.Blur();
        }

        internal void CloseInternal()
        {
            if (!IsOpen) return;
            BlurInternal();
            IsOpen = false;
            View?.CloseInternal();
            Closed?.Invoke(this);
        }

        internal void DisposeContent()
        {
            try { _logic?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name}: logic dispose failed.", exception); }
            try { _binding?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name}: binding dispose failed.", exception); }
            finally
            {
                _logic = null;
                _binding = null;
            }
        }

        private void OnDestroy()
        {
            DisposeContent();
            UIManager manager = Manager;
            Manager = null;
            manager?.NotifyViewDestroyed(this);
        }
    }
}
