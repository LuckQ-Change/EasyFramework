using System;
using UnityEngine;

namespace EasyFramework.Tweening
{
    /// <summary>一个动画的播放状态和生命周期；具体属性插值由子类负责。</summary>
    public abstract class TweenHandle
    {
        private Action _completed;
        private Action _killed;
        private UnityEngine.Object _owner;
        private bool _ownerBound;
        private bool _initialized;
        private bool _embedded;
        private int _playbackRevision;
        private TweenQueue _queue;

        protected TweenHandle(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f)
                throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = duration;
        }

        public float Duration { get; protected set; }
        public float Elapsed { get; protected set; }
        public bool IsPlaying { get; protected set; }
        public bool IsComplete { get; protected set; }
        public bool IsKilled { get; protected set; }
        public TweenEase Ease { get; private set; } = TweenEase.Linear;
        public TweenTimeMode TimeMode { get; private set; } = TweenTimeMode.Scaled;

        #region 公共方法

        /// <summary>选择当前动画的插值曲线。</summary>
        public TweenHandle SetEase(TweenEase ease) { Ease = ease; return this; }
        /// <summary>选择调度时读取的时间增量；Sequence 的时间模式由父级决定。</summary>
        public TweenHandle SetTimeMode(TweenTimeMode mode) { TimeMode = mode; return this; }
        /// <summary>绑定 Unity 对象；对象销毁后，下一次 Tick 将停止动画。</summary>
        public TweenHandle SetOwner(UnityEngine.Object owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
            _ownerBound = true;
            return this;
        }
        /// <summary>完成时调用；Kill 不会调用。</summary>
        public TweenHandle OnComplete(Action callback) { _completed += callback; return this; }
        /// <summary>提前停止时调用；正常完成不会调用。</summary>
        public TweenHandle OnKill(Action callback) { _killed += callback; return this; }

        /// <summary>首次在当前宿主播放，或在首次绑定的宿主恢复播放。</summary>
        /// <returns>用于继续控制播放的动画句柄。</returns>
        public TweenHandle Play() => PlayOn(null);

        internal TweenHandle PlayOn(TweenQueue queue)
        {
            if (_embedded) throw new InvalidOperationException("A tween in a Sequence cannot be played separately.");
            if (_queue != null && queue != null && !ReferenceEquals(_queue, queue))
                throw new InvalidOperationException("已经播放过的动画不能迁移到其他宿主。");
            if (IsKilled || IsComplete) return this;

            // 先固定首次选定的宿主，保证初始写值若重入同一动画时仍使用原调度器。
            _queue ??= queue ?? TweenScheduler.ResolveQueue();
            _queue.Play(this);
            return this;
        }

        /// <summary>暂停但保留进度和当前属性值。</summary>
        public virtual TweenHandle Pause()
        {
            IsPlaying = false;
            _playbackRevision++;
            return this;
        }

        /// <summary>恢复起点并重新播放；Sequence 会同时重置子动画。</summary>
        public TweenHandle Restart()
        {
            if (_embedded) throw new InvalidOperationException("Restart the owning Sequence instead.");
            if (IsKilled) return this;
            using var scope = _queue?.EnterScopeIfNeeded();
            ResetEmbedded();
            return Play();
        }

        /// <summary>设置画面与进度，不派发完成回调。供编辑器拖动预览使用。</summary>
        public virtual TweenHandle Seek(float seconds)
        {
            if (IsKilled) return this;
            if (IsPlaying) throw new InvalidOperationException("Pause a tween before seeking it.");
            _playbackRevision++;
            SetPosition(seconds);
            if (!IsKilled) OnSilentSeek(Elapsed);
            return this;
        }

        internal void SetPosition(float seconds)
        {
            if (float.IsNaN(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (OwnerMissing) { Kill(); return; }
            Elapsed = Mathf.Clamp(seconds, 0f, Duration);
            ApplyAt(Elapsed);
            _initialized = true;
        }

        /// <summary>写入终值并派发一次完成回调。</summary>
        public virtual void Complete()
        {
            if (IsKilled || IsComplete) return;
            SetPosition(Duration);
            Finish();
        }

        /// <summary>立即停止；不写入终值，也不派发完成回调。</summary>
        public virtual void Kill()
        {
            if (IsKilled || IsComplete) return;
            IsKilled = true;
            IsPlaying = false;
            _playbackRevision++;
            InvokeSafely(_killed, "kill");
        }

        #endregion

        #region 生命周期

        internal void StartEmbedded()
        {
            if (IsKilled || IsComplete) return;
            if (OwnerMissing) { Kill(); return; }
            if (!_initialized) SetPosition(0f);
            if (IsKilled || IsComplete || IsPlaying) return;
            IsPlaying = true;
            int revision = _playbackRevision;
            if (OnPlaybackSampled() && IsPlaying && revision == _playbackRevision && Duration <= 0f)
                Complete();
        }

        internal void Adopt()
        {
            if (_embedded || _queue != null || IsPlaying || IsComplete || IsKilled)
                throw new InvalidOperationException("Only a new, stopped tween can be added to a Sequence.");
            _embedded = true;
        }

        internal void ResetEmbedded()
        {
            IsPlaying = false;
            IsComplete = false;
            Elapsed = 0f;
            _initialized = false;
            _playbackRevision++;
            ResetChildren();
        }

        internal void MarkCompleteSilently()
        {
            IsPlaying = false;
            IsComplete = true;
        }

        internal virtual void Tick(float deltaTime)
        {
            if (!IsPlaying || IsKilled || IsComplete) return;
            if (OwnerMissing) { Kill(); return; }

            int revision = _playbackRevision;
            SetPosition(Elapsed + Mathf.Max(0f, deltaTime));
            // 播放回调可以暂停、停止或重播。旧采样不应结束回调中新开始的播放。
            if (!IsPlaying || revision != _playbackRevision) return;
            if (OnPlaybackSampled() && IsPlaying && revision == _playbackRevision && Elapsed >= Duration)
                Finish();
        }

        protected float EaseAt(float seconds) =>
            TweenEaseUtility.Evaluate(Ease, Duration <= 0f ? 1f : seconds / Duration);

        protected void Finish()
        {
            if (IsKilled || IsComplete) return;
            IsComplete = true;
            IsPlaying = false;
            InvokeSafely(_completed, "complete");
        }

        protected bool OwnerMissing => _ownerBound && _owner == null;
        /// <summary>播放控制的修订号，用于检测回调中的定位、暂停、停止与重播。</summary>
        protected int PlaybackRevision => _playbackRevision;
        /// <summary>在自然播放从当前进度启动或推进时处理采样后的逻辑。</summary>
        /// <returns>本次采样是否已处理完毕并可判断自然完成。</returns>
        protected virtual bool OnPlaybackSampled() => true;
        /// <summary>在主动定位完成后，同步派生播放器的游标。</summary>
        /// <param name="seconds">夹取到有效时间范围后的进度。</param>
        protected virtual void OnSilentSeek(float seconds) { }
        protected virtual void ResetChildren() { }
        protected abstract void ApplyAt(float seconds);

        #endregion

        #region 私有逻辑

        private static void InvokeSafely(Action callback, string phase)
        {
            if (callback == null) return;
            foreach (Delegate handler in callback.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception exception) { Log.Error($"[Tween] {phase} callback failed.", exception); }
            }
        }

        #endregion
    }
}
