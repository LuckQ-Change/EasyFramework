using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.Tweening
{
    /// <summary>以时间偏移编排多个 Tween；子 Tween 由 Sequence 独占推进。</summary>
    public sealed class TweenSequence : TweenHandle
    {
        private sealed class Track
        {
            public TweenHandle Tween;
            public float Start;
            public int Order;
            public bool Started;
        }

        private readonly List<Track> _tracks = new List<Track>();
        private float _lastStart;

        internal TweenSequence() : base(0f) { }

        /// <summary>在当前结尾加入子动画，所有权移交给 Sequence。</summary>
        public TweenSequence Append(TweenHandle tween) => InsertAt(Duration, tween);

        /// <summary>与上一个子动画同时开始。</summary>
        public TweenSequence Join(TweenHandle tween) => InsertAt(_lastStart, tween);

        /// <summary>在指定时间加入子动画，允许重叠。</summary>
        public TweenSequence Insert(float time, TweenHandle tween) => InsertAt(time, tween);

        /// <summary>在时间轴末尾加入空白时段。</summary>
        public TweenSequence AppendInterval(float seconds)
        {
            EnsureEditable();
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            _lastStart = Duration;
            Duration += seconds;
            return this;
        }

        public override TweenHandle Pause()
        {
            base.Pause();
            foreach (Track track in _tracks)
                if (track.Started) track.Tween.Pause();
            return this;
        }

        public override TweenHandle Seek(float seconds)
        {
            if (IsPlaying) throw new InvalidOperationException("Pause a Sequence before seeking it.");
            ResetChildren();
            return base.Seek(seconds);
        }

        internal override void Tick(float deltaTime)
        {
            if (!IsPlaying || IsKilled || IsComplete) return;
            if (OwnerMissing) { Kill(); return; }

            float previous = Elapsed;
            float current = Mathf.Min(Duration, previous + Mathf.Max(0f, deltaTime));
            for (int i = 0; i < _tracks.Count; i++)
            {
                Track track = _tracks[i];
                if (current < track.Start || track.Tween.IsKilled) continue;
                if (!track.Started)
                {
                    // Seek 越过的子动画只同步画面；正常抵达的零时长节点仍需派发完成。
                    float end = track.Start + track.Tween.Duration;
                    if (previous > end || (track.Tween.Duration > 0f && previous >= end))
                    {
                        track.Tween.SetPosition(track.Tween.Duration);
                        track.Tween.MarkCompleteSilently();
                        track.Started = true;
                        continue;
                    }
                    track.Tween.SetPosition(Mathf.Max(0f, previous - track.Start));
                    track.Tween.StartEmbedded();
                    track.Started = true;
                }
                else if (!track.Tween.IsPlaying && !track.Tween.IsComplete)
                    track.Tween.StartEmbedded();
                if (track.Tween.IsComplete) continue;
                float overlap = Mathf.Min(current, track.Start + track.Tween.Duration)
                    - Mathf.Max(previous, track.Start);
                if (overlap >= 0f) track.Tween.Tick(overlap);
            }

            Elapsed = current;
            if (Elapsed >= Duration) Finish();
        }

        public override void Complete()
        {
            if (IsKilled || IsComplete) return;
            for (int i = 0; i < _tracks.Count; i++) _tracks[i].Tween.Complete();
            base.Complete();
        }

        public override void Kill()
        {
            if (IsKilled || IsComplete) return;
            for (int i = 0; i < _tracks.Count; i++) _tracks[i].Tween.Kill();
            base.Kill();
        }

        protected override void ResetChildren()
        {
            foreach (Track track in _tracks)
            {
                track.Tween.ResetEmbedded();
                track.Started = false;
            }
        }

        protected override void ApplyAt(float seconds)
        {
            // Future tracks are reset first, so seeking backwards restores their initial values.
            for (int i = _tracks.Count - 1; i >= 0; i--)
                if (seconds < _tracks[i].Start) _tracks[i].Tween.SetPosition(0f);
            for (int i = 0; i < _tracks.Count; i++)
                if (seconds >= _tracks[i].Start)
                    _tracks[i].Tween.SetPosition(seconds - _tracks[i].Start);
        }

        private TweenSequence InsertAt(float time, TweenHandle tween)
        {
            EnsureEditable();
            if (tween == null) throw new ArgumentNullException(nameof(tween));
            if (ReferenceEquals(tween, this)) throw new InvalidOperationException("A Sequence cannot contain itself.");
            if (tween is TweenSequence nested && nested.Contains(this))
                throw new InvalidOperationException("Nested Sequences cannot form a cycle.");
            if (float.IsNaN(time) || float.IsInfinity(time) || time < 0f)
                throw new ArgumentOutOfRangeException(nameof(time));
            tween.Adopt();
            _tracks.Add(new Track { Tween = tween, Start = time, Order = _tracks.Count });
            _tracks.Sort((a, b) =>
            {
                int startOrder = a.Start.CompareTo(b.Start);
                return startOrder != 0 ? startOrder : a.Order.CompareTo(b.Order);
            });
            _lastStart = time;
            Duration = Mathf.Max(Duration, time + tween.Duration);
            return this;
        }

        private void EnsureEditable()
        {
            if (IsPlaying || IsComplete || IsKilled || Elapsed > 0f)
                throw new InvalidOperationException("A Sequence can only be edited before playback.");
        }

        private bool Contains(TweenHandle target)
        {
            foreach (Track track in _tracks)
            {
                if (ReferenceEquals(track.Tween, target)) return true;
                if (track.Tween is TweenSequence nested && nested.Contains(target)) return true;
            }
            return false;
        }
    }
}
