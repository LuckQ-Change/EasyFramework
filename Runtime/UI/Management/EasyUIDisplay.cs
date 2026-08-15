using System;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>
    /// Universal prefab display. It creates recorded pure C# View/Item and Binding objects.
    /// </summary>
    [AddComponentMenu("EasyFramework/UI/UI Display")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class EasyUIDisplay : MonoBehaviour
    {
        [Header("View Script")]
        [SerializeField] private string _viewTypeName;
        [SerializeField] private string _viewScriptPath;
        [SerializeField] private string _prefabLocation;
        [SerializeField] private bool _createOnAwake = true;

        [Header("View")]
        [SerializeField] private bool _managedAsView = true;
        [SerializeField] private string _viewId;
        [SerializeField] private UILayer _defaultLayer = UILayer.Screen;
        [SerializeField] private bool _singleInstance = true;
        [SerializeField] private bool _closeOnBack = true;
        [SerializeField] private bool _stretchToLayer = true;

        [Header("Shared Background")]
        [SerializeField] private UIBackgroundMode _backgroundMode;
        [SerializeField] private bool _closeOnBackground;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.55f);

        private EasyUIObject _logic;
        private EasyUIBinding _binding;
        private UIBindingContext _bindingContext;
        private bool _created;
        private bool _focused;

        public string ViewTypeName => _viewTypeName;
        public string ViewScriptPath => _viewScriptPath;
        public string PrefabLocation => _prefabLocation;
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
        public bool IsFocused => _focused;
        public object OpenArgs { get; private set; }
        public EasyUIManager Manager { get; private set; }
        public EasyUIObject Logic => EnsureCreated();
        public EasyUIView View => EnsureCreated() as EasyUIView;
        public EasyUIBinding Binding
        {
            get
            {
                EnsureCreated();
                return _binding;
            }
        }
        public UIBindingContext BindingContext =>
            _bindingContext == null ? (_bindingContext = GetComponent<UIBindingContext>()) : _bindingContext;

        public event Action<EasyUIDisplay, object> Opened;
        public event Action<EasyUIDisplay> Closed;

        private void Awake()
        {
            if (_createOnAwake) EnsureCreated();
        }

        public T GetLogic<T>() where T : EasyUIObject => EnsureCreated() as T;
        public void Close() => Manager?.Close(this);
        public void BringToFront() => Manager?.BringToFront(this);
        public void SetLayer(UILayer layer) => Manager?.SetLayer(this, layer);

        public EasyUIObject EnsureCreated()
        {
            if (_logic != null) return _logic;
            if (!EasyUIFactory.TryCreate(_viewTypeName, out _logic, out _binding))
            {
                if (!string.IsNullOrWhiteSpace(_viewTypeName))
                    throw new InvalidOperationException(
                        $"{name}: cannot create UI View '{_viewTypeName}'. Resolve its compilation errors.");
                _binding = new EasyUIBinding();
                _logic = _managedAsView ? (EasyUIObject)new EasyUIView() : new EasyUIItem();
            }

            try
            {
                _binding.Initialize(this);
                _logic.Initialize(this, _binding);
                // A managed View owns its binding source. By default that source is
                // the View itself, while advanced Views may override BindingSource.
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

        internal void Initialize(EasyUIManager manager, UILayer layer)
        {
            Manager = manager;
            CurrentLayer = layer;
            if (_created) return;
            _created = true;
            EasyUIObject logic = EnsureCreated();
            if (_managedAsView && !(logic is EasyUIView))
                throw new InvalidOperationException(
                    $"{name} is managed as a view, but {logic.GetType().Name} does not inherit EasyUIView.");
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
            catch (Exception exception) { Log.Error($"[UI] {name}: logic dispose failed: {exception}"); }
            try { _binding?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name}: binding dispose failed: {exception}"); }
            finally
            {
                _logic = null;
                _binding = null;
            }
        }

        private void OnDestroy()
        {
            DisposeContent();
            EasyUIManager manager = Manager;
            Manager = null;
            manager?.NotifyViewDestroyed(this);
        }
    }
}
