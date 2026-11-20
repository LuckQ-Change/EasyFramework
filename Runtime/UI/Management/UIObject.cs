using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    public abstract class UIObject : IDisposable
    {
        private readonly CompositeDisposable _lifetime = new();
        private CompositeDisposable _visible;
        private AssetScope _assets;
        private UISpriteSlots _sprites;
        private AudioSoundScope _sounds;
        private List<UINode> _nodes;

        protected UIBindingRoot Display { get; private set; }
        internal bool IsDisposed { get; private set; }
        internal bool IsVisible => _visible != null;

        internal void Initialize(UIBindingRoot display)
        {
            Display = display;
            BindControls();
            OnCreated();
        }

        protected virtual void BindControls() { }
        protected virtual void OnCreated() { }
        protected virtual void OnHidden() { }
        protected virtual void OnDispose() { }

        protected T Control<T>(int index, string name) where T : Component
        {
            var slots = Display.Controls;
            if (index >= 0 && index < slots.Count && slots[index] is UIControlReference slot && slot.Name == name &&
                slot.Target is T component && component != null)
                return component;
            throw new InvalidOperationException($"{Display.name} 的控件引用 {name} 未同步或缺失。");
        }

        protected T BindNode<T>(int index, string name) where T : UINodeView, new()
        {
            UINode node = Control<UINode>(index, name);
            if (node.ParentBindingRoot != Display)
                throw new InvalidOperationException($"{name} 需要位于 {Display.name} 的直接绑定范围内。");
            T view = node.Attach<T>(this);
            (_nodes ??= new List<UINode>()).Add(node);
            return view;
        }

        protected void Click(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _lifetime.Add(new ActionDisposable(() =>
            {
                if (button != null) button.onClick.RemoveListener(action);
            }));
        }

        protected void Watch<T>(ReactiveProperty<T> source, Action<T> update)
        {
            CompositeDisposable visible = _visible ??
                throw new InvalidOperationException("请在界面的 Refresh 回调中订阅可见期数据。");
            visible.Add(source.Subscribe(update));
        }

        protected Task<T> LoadAssetAsync<T>(string address, CancellationToken token = default)
            where T : UnityEngine.Object
        {
            if (IsDisposed) throw new ObjectDisposedException(GetType().Name);
            _assets ??= UIManager.RequireAssets().CreateScope();
            return _assets.LoadAsync<T>(address, token);
        }

        /// <summary>设置可替换的图片。新图成功后归还旧图，隐藏或回收时归还当前图片。</summary>
        protected Task<bool> SetSpriteAsync(Image image, string address, CancellationToken token = default)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (IsDisposed) throw new ObjectDisposedException(GetType().Name);
            if (!IsVisible) return Task.FromResult(false);
            _sprites ??= new UISpriteSlots();
            return _sprites.SetAsync(image, address, token);
        }

        internal void ReleaseTransientSprites() => _sprites?.Release();

        /// <summary>播放界面音效。隐藏或刷新后取消当前界面的待播请求。</summary>
        protected Task<long> PlaySoundAsync(string address, float volume = 1f, bool feedback = false,
            CancellationToken token = default)
        {
            if (!IsVisible) return Task.FromResult(0L);
            AudioModule audio = AudioModule.Instance;
            if (audio == null) return Task.FromResult(0L);
            if (_sounds == null)
            {
                _sounds = audio.CreateSoundScope();
                _visible.Add(_sounds);
            }
            return _sounds.PlayAsync(address, volume, feedback, token);
        }

        internal void AddResourceUsage(ref int leases, ref long estimatedBytes)
        {
            _assets?.AddUsage(ref leases, ref estimatedBytes);
            _sprites?.AddUsage(ref leases, ref estimatedBytes);
        }

        internal void ReleaseAssets()
        {
            AssetScope scope = _assets;
            _assets = null;
            scope?.Dispose();
        }

        internal void Render(Action refresh)
        {
            _sprites?.CancelPending();
            _visible?.Dispose();
            _sounds = null;
            _visible = new CompositeDisposable();
            refresh();
            if (_nodes != null)
                foreach (UINode node in _nodes)
                    if (node != null) node.ResumeView();
        }

        internal void Suspend()
        {
            _sprites?.CancelPending();
            CompositeDisposable scope = _visible;
            _visible = null;
            _sounds = null;
            scope?.Dispose();
            if (_nodes != null)
                foreach (UINode node in _nodes)
                    if (node != null) node.SuspendView();
            if (scope != null) OnHidden();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { Suspend(); }
            finally
            {
                try { OnDispose(); }
                finally
                {
                    if (_nodes != null)
                        foreach (UINode node in _nodes)
                            if (node != null) node.DisposeView();
                    _nodes = null;
                    _lifetime.Dispose();
                    ReleaseTransientSprites();
                    ReleaseAssets();
                    Display = null;
                }
            }
        }
    }

    public class UIItem : UIObject
    {
        protected virtual void Refresh() { }
        public void Rebind()
        {
            ReleaseAssets();
            Render(Refresh);
        }

        internal virtual void Recycle()
        {
            try { Suspend(); }
            finally
            {
                ReleaseTransientSprites();
                ReleaseAssets();
            }
        }
    }

    public abstract class UIListItem<T> : UIItem
    {
        protected T Data { get; private set; }
        protected UIVirtualListItemContext Row { get; private set; }

        internal void Bind(T data, UIVirtualListItemContext context)
        {
            Data = data;
            Row = context;
            Rebind();
        }

        internal override void Recycle()
        {
            try { base.Recycle(); }
            finally
            {
                Data = default;
                Row = default;
            }
        }
    }
}
