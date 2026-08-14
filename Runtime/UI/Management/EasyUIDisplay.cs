using System;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>
    /// Universal prefab display. It creates recorded pure C# View/Item and Binding objects.
    /// </summary>
    [AddComponentMenu("EasyFramework/UI/UI Display")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIBindingContext))]
    [DefaultExecutionOrder(-200)]
    public sealed class EasyUIDisplay : MonoBehaviour
    {
        [Header("Scripts")]
        [SerializeField] private EasyUIScriptRecord _scripts = new EasyUIScriptRecord();
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

        public EasyUIScriptRecord Scripts => _scripts;
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
        public EasyUIBinding GeneratedBinding
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
            if (!EasyUIFactoryRegistry.TryCreate(_scripts, out _logic, out _binding))
            {
                if (!string.IsNullOrWhiteSpace(_scripts.LogicTypeName))
                    throw new InvalidOperationException(
                        $"{name}: cannot create UI logic '{_scripts.LogicTypeName}'. " +
                        "Regenerate the Display scripts and resolve compilation errors.");
                _binding = new EmptyEasyUIBinding();
                _logic = _managedAsView ? (EasyUIObject)new EasyUIView() : new EasyUIItem();
            }

            try
            {
                _binding.Initialize(this);
                _logic.Initialize(this, _binding);
                if (BindingContext != null && BindingContext.Source == null)
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

        public void SetScriptRecord(
            string recordId,
            string logicTypeName,
            string bindingTypeName,
            string logicBaseTypeName,
            string logicScriptPath,
            string bindingScriptPath,
            string displaySignature,
            string prefabLocation)
        {
            _scripts.Set(
                recordId, logicTypeName, bindingTypeName, logicBaseTypeName,
                logicScriptPath, bindingScriptPath, displaySignature);
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

        private void OnDestroy()
        {
            try { _logic?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name}: logic dispose failed: {exception}"); }
            try { _binding?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name}: binding dispose failed: {exception}"); }
            finally
            {
                _logic = null;
                _binding = null;
                EasyUIManager manager = Manager;
                Manager = null;
                manager?.NotifyViewDestroyed(this);
            }
        }
    }
}
