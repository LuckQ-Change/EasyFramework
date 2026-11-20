using System;
using UnityEngine;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Node")]
    public sealed class UINode : UIBindingRoot
    {
        private UINodeView _view;
        private UIObject _owner;

        public override UIPageKind Kind => UIPageKind.Node;
        internal override UIObject RuntimeView => _view;
        internal bool CanRender => isActiveAndEnabled && _owner?.IsVisible == true;

        internal T Attach<T>(UIObject owner) where T : UINodeView, new()
        {
            if (_owner == owner && _view is T existing) return existing;
            if (_owner != null || UIFactory.ResolveType(ViewTypeName) != typeof(T))
                throw new InvalidOperationException($"{name} 未配置为 {typeof(T).Name}，或已经绑定到其他 UI。");

            var node = new T();
            _owner = owner;
            _view = node;
            try { node.Initialize(this); }
            catch { DisposeView(); throw; }
            return node;
        }

        internal void ResumeView()
        {
            if (!CanRender || _view == null || _view.IsVisible) return;
            _view.Resume();
        }

        internal void SuspendView()
        {
            try { _view?.Pause(); }
            catch (Exception exception) { Log.Error($"[UI] {name} 隐藏失败。", exception); }
        }

        internal void DisposeView()
        {
            UINodeView view = _view;
            _view = null;
            _owner = null;
            try { view?.Dispose(); }
            catch (Exception exception) { Log.Error($"[UI] {name} 释放失败。", exception); }
        }

        private void OnEnable()
        {
            try { ResumeView(); }
            catch (Exception exception) { Log.Error($"[UI] {name} 刷新失败。", exception); }
        }

        private void OnDisable()
        {
            SuspendView();
            _view?.ReleaseTransientSprites();
        }
        private void OnDestroy() => DisposeView();
    }

    public abstract class UINodeView : UIObject
    {
        protected virtual void Refresh() { }

        protected void Rebind()
        {
            if (Display is UINode node && node.CanRender) Resume();
        }

        internal void Resume()
        {
            ReleaseAssets();
            Render(Refresh);
        }

        internal void Pause()
        {
            try { Suspend(); }
            finally { ReleaseAssets(); }
        }
    }
}
