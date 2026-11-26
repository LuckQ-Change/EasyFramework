using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework.UI
{
    public enum UIPageKind { Screen, Popup, Item, Node }

    [Serializable]
    public sealed class UIControlReference
    {
        [SerializeField] private string _name;
        [SerializeField] private Component _target;
        public string Name => _name;
        public Component Target => _target;

        public UIControlReference(string name, Component target)
        {
            _name = name;
            _target = target;
        }
    }

    /// <summary>连接页面或列表项 Prefab 与它的运行时 View。</summary>
    [AddComponentMenu("EasyFramework/UI/Display")]
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class UIDisplay : UIBindingRoot
    {
        [SerializeField] private UIPageKind _kind;
        [SerializeField] private Color _backgroundColor = new(0f, 0f, 0f, 0.55f);
        private UIObject _logic;
        private UIManager _manager;

        /// <summary>此 Prefab 使用的页面或列表项类型。</summary>
        public override UIPageKind Kind => _kind;
        /// <summary>弹窗共享遮罩所用的颜色。</summary>
        public Color BackgroundColor => _backgroundColor;
        internal override UIObject RuntimeView => _logic;
        internal bool IsClosing { get; set; }

        /// <summary>配置 Prefab 的界面类型和 View 绑定。</summary>
        /// <param name="kind">页面或列表项的类型。</param>
        /// <param name="typeName">View 的完整类型名。</param>
        /// <param name="scriptGuid">View 脚本的资产 GUID。</param>
        public void Configure(UIPageKind kind, string typeName, string scriptGuid)
        {
            if (kind == UIPageKind.Node) throw new ArgumentException("请用 UINode 挂载独立组件。", nameof(kind));
            _kind = kind;
            SetViewScript(typeName, scriptGuid);
        }

        public T GetItem<T>() where T : UIItem
        {
            if (_kind != UIPageKind.Item) throw new InvalidOperationException($"{name} 不是 UI Item。");
            if (_logic is T existing) return existing;
            if (_logic != null) throw new InvalidOperationException($"{name} 已绑定其他 Item 类型。");
            Type type = UIFactory.ResolveType(ViewTypeName);
            if (type == null || !typeof(T).IsAssignableFrom(type))
                throw new InvalidOperationException($"{name} 不是 {typeof(T).Name}。");
            var item = (T)Activator.CreateInstance(type);
            _logic = item;
            try { item.Initialize(this); }
            catch { DisposeContent(); throw; }
            return item;
        }

        internal void Attach(UIManager manager, UIView view)
        {
            _manager = manager;
            _logic = view;
        }

        internal Task CloseAsync() => _manager?.CloseAsync(this) ?? Task.CompletedTask;

        private void OnDisable()
        {
            RecycleItem();
            if (IsPage) _logic?.ReleaseTransientSprites();
        }

        internal void RecycleItem()
        {
            if (!(_logic is UIItem item)) return;
            try { item.Recycle(); }
            catch (Exception exception) { Log.Error($"[UI] {name} 回收失败。", exception); }
        }

        internal void DisposeContent()
        {
            try { _logic?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name} 释放失败。", exception); }
            finally { _logic = null; }
        }

        private void OnDestroy()
        {
            DisposeContent();
            _manager?.NotifyDestroyed(this);
        }
    }
}
