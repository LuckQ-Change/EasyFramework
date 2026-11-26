using System;
using System.Collections.Generic;

namespace EasyFramework.Tweening
{
    /// <summary>在当前宿主或独立编辑环境中手动操作 Tween 调度。</summary>
    public static class TweenScheduler
    {
        private static readonly TweenQueue _manualQueue = new TweenQueue();

        /// <summary>当前调度器中正在播放的动画数量。</summary>
        public static int ActiveCount => ResolveQueue().ActiveCount;

        /// <summary>手动推进当前调度器一帧。</summary>
        /// <param name="scaledDeltaTime">受时间缩放影响的帧时间。</param>
        /// <param name="unscaledDeltaTime">未缩放的帧时间。</param>
        public static void Tick(float scaledDeltaTime, float unscaledDeltaTime) =>
            ResolveQueue().Tick(scaledDeltaTime, unscaledDeltaTime);

        /// <summary>停止当前调度器中的全部动画。</summary>
        public static void KillAll() => ResolveQueue().Clear();

        internal static TweenQueue ResolveQueue()
        {
            ModuleManager host = ModuleManager.Current;
            if (host == null) return _manualQueue;
            return host.Get<TweenModule>()?.Queue ??
                   throw new InvalidOperationException("请在当前宿主装配 TweenModule 后播放动画。");
        }
    }

    /// <summary>保存一个宿主或独立编辑环境的动画队列。</summary>
    internal sealed class TweenQueue
    {
        private readonly List<TweenHandle> _active = new List<TweenHandle>();
        private readonly List<TweenHandle> _pending = new List<TweenHandle>();
        private ModuleManager _host;
        private bool _isUpdating;
        private bool _isClearing;
        private bool _isClosed;
        private int _clearVersion;

        internal int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (TweenHandle tween in _active)
                    if (tween.IsPlaying && !tween.IsKilled && !tween.IsComplete) count++;
                foreach (TweenHandle tween in _pending)
                    if (tween.IsPlaying && !tween.IsKilled && !tween.IsComplete) count++;
                return count;
            }
        }

        internal void BindHost(ModuleManager host) => _host = host;

        internal IDisposable EnterScopeIfNeeded() =>
            _host != null && !ReferenceEquals(_host, ModuleManager.Current) ? _host.EnterScope() : null;

        internal void Play(TweenHandle tween)
        {
            // 暂停的动画可在别的宿主调用链中恢复，初始写值和回调仍归原宿主。
            using var scope = EnterScopeIfNeeded();
            if (_isClosed || _isClearing)
            {
                tween.Kill();
                return;
            }

            int clearVersion = _clearVersion;
            List<TweenHandle> added = null;
            if (!_active.Contains(tween) && !_pending.Contains(tween))
            {
                // 起点回调可能立即取消整个队列。启动中的句柄也必须参与取消。
                added = _isUpdating ? _pending : _active;
                added.Add(tween);
            }
            try { tween.StartEmbedded(); }
            catch
            {
                added?.Remove(tween);
                throw;
            }

            if (_isClosed || clearVersion != _clearVersion)
                tween.Kill();
            if (tween.IsKilled || tween.IsComplete) added?.Remove(tween);
        }

        internal void Tick(float scaledDeltaTime, float unscaledDeltaTime)
        {
            if (_isUpdating) throw new InvalidOperationException("TweenScheduler.Tick cannot be called recursively.");
            if (_isClosed || _isClearing) return;
            if (float.IsNaN(scaledDeltaTime) || float.IsInfinity(scaledDeltaTime) || scaledDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(scaledDeltaTime));
            if (float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime) || unscaledDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(unscaledDeltaTime));
            _isUpdating = true;
            try
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    TweenHandle tween = _active[i];
                    if (!tween.IsPlaying || tween.IsKilled || tween.IsComplete) continue;
                    try
                    {
                        tween.Tick(tween.TimeMode == TweenTimeMode.Unscaled
                            ? unscaledDeltaTime : scaledDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("[Tween] update failed.", exception);
                        KillSafely(tween);
                    }
                }
            }
            finally
            {
                _isUpdating = false;
                int retained = 0;
                for (int i = 0; i < _active.Count; i++)
                {
                    TweenHandle tween = _active[i];
                    if (!tween.IsKilled && !tween.IsComplete) _active[retained++] = tween;
                }
                if (retained < _active.Count) _active.RemoveRange(retained, _active.Count - retained);
                foreach (TweenHandle tween in _pending)
                    if (!tween.IsKilled && !tween.IsComplete) _active.Add(tween);
                _pending.Clear();
            }
        }

        internal void Close()
        {
            _isClosed = true;
            try { Clear(); }
            finally { _host = null; }
        }

        internal void Clear()
        {
            if (_isClearing) return;
            _isClearing = true;
            _clearVersion++;
            try
            {
                foreach (TweenHandle tween in _active) KillSafely(tween);
                foreach (TweenHandle tween in _pending) KillSafely(tween);
            }
            finally
            {
                _active.Clear();
                _pending.Clear();
                _isClearing = false;
            }
        }

        private static void KillSafely(TweenHandle tween)
        {
            try { tween.Kill(); }
            catch (Exception exception) { Log.Error("[Tween] 停止动画失败。", exception); }
        }
    }
}
