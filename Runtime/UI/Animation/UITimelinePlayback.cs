using System;
using System.Collections.Generic;
using System.Linq;
using EasyFramework.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    /// <summary>把某个时间轴属性和实际组件绑定在一起。</summary>
    internal readonly struct UITimelineBinding : IEquatable<UITimelineBinding>
    {
        internal readonly Component Target;
        internal readonly UITimelineProperty Property;

        internal UITimelineBinding(Component target, UITimelineProperty property)
        {
            Target = target;
            Property = property;
        }

        internal static Type TargetType(UITimelineProperty property) => property switch
        {
            UITimelineProperty.Color => typeof(Graphic),
            UITimelineProperty.Alpha => typeof(CanvasGroup),
            UITimelineProperty.FillAmount => typeof(Image),
            _ => typeof(RectTransform),
        };

        internal Vector4 Read()
        {
            return Property switch
            {
                UITimelineProperty.Color => (Vector4)((Graphic)Target).color,
                UITimelineProperty.AnchoredPosition => ((RectTransform)Target).anchoredPosition,
                UITimelineProperty.Scale => ((Transform)Target).localScale,
                UITimelineProperty.Alpha => new Vector4(((CanvasGroup)Target).alpha, 0f, 0f, 0f),
                UITimelineProperty.Rotation => new Vector4(((RectTransform)Target).localEulerAngles.z, 0f, 0f, 0f),
                UITimelineProperty.FillAmount => new Vector4(((Image)Target).fillAmount, 0f, 0f, 0f),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        internal void Write(Vector4 value)
        {
            if (Target == null) return;
            switch (Property)
            {
                case UITimelineProperty.Color:
                    ((Graphic)Target).color = value;
                    break;
                case UITimelineProperty.AnchoredPosition:
                    ((RectTransform)Target).anchoredPosition = new Vector2(value.x, value.y);
                    break;
                case UITimelineProperty.Scale:
                    ((Transform)Target).localScale = value;
                    break;
                case UITimelineProperty.Alpha:
                    ((CanvasGroup)Target).alpha = Mathf.Clamp01(value.x);
                    break;
                case UITimelineProperty.Rotation:
                    var target = (RectTransform)Target;
                    Vector3 angles = target.localEulerAngles;
                    angles.z = value.x;
                    target.localEulerAngles = angles;
                    break;
                case UITimelineProperty.FillAmount:
                    ((Image)Target).fillAmount = Mathf.Clamp01(value.x);
                    break;
            }
        }

        /// <summary>判断目标组件和属性是否相同。</summary>
        /// <param name="other">要比较的属性绑定。</param>
        /// <returns>绑定相同时返回 true。</returns>
        public bool Equals(UITimelineBinding other) => Target == other.Target && Property == other.Property;
        /// <summary>判断对象是否为相同的属性绑定。</summary>
        /// <param name="obj">要比较的对象。</param>
        /// <returns>绑定相同时返回 true。</returns>
        public override bool Equals(object obj) => obj is UITimelineBinding other && Equals(other);
        /// <summary>生成以组件和属性为键的哈希值。</summary>
        /// <returns>属性绑定的哈希值。</returns>
        public override int GetHashCode() => HashCode.Combine(Target, Property);
        /// <summary>取得供工具界面展示的目标名与属性名。</summary>
        /// <returns>属性绑定的简要描述。</returns>
        public override string ToString() => $"{(Target == null ? "未绑定" : Target.name)} · {Property}";
    }

    /// <summary>一个属性在指定时间区间内的插值数据。</summary>
    internal readonly struct UITimelineSpan
    {
        internal readonly float Start;
        internal readonly float End;
        internal readonly Vector4 From;
        internal readonly Vector4 To;
        internal readonly TweenEase Ease;

        internal UITimelineSpan(float start, float end, Vector4 from, Vector4 to, TweenEase ease)
        {
            Start = start;
            End = end;
            From = from;
            To = to;
            Ease = ease;
        }

        internal Vector4 Evaluate(float time)
        {
            if (time >= End) return To;
            float progress = (time - Start) / (End - Start);
            return Vector4.LerpUnclamped(From, To, TweenEaseUtility.Evaluate(Ease, progress));
        }
    }

    /// <summary>按绝对时间取样 UI 属性，并在自然播放中按序派发事件。</summary>
    internal sealed class UITimelinePlayback : TweenHandle
    {
        /// <summary>保存同一目标属性的全部插值区间。</summary>
        private sealed class Channel
        {
            internal readonly UITimelineBinding Binding;
            internal readonly List<UITimelineSpan> Spans = new();

            internal Channel(UITimelineBinding binding) => Binding = binding;

            internal Vector4 Evaluate(float time)
            {
                if (time < Spans[0].Start) return Spans[0].From;
                int low = 0;
                int high = Spans.Count - 1;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (Spans[middle].Start <= time) low = middle;
                    else high = middle - 1;
                }
                return Spans[low].Evaluate(time);
            }
        }

        private readonly Channel[] _channels;
        private readonly UITimelineEvent[] _events;
        private readonly UITimeline _eventSource;
        private readonly UIDisplay _page;
        private readonly bool _isClosingTransition;
        private Vector4[] _snapshot;
        private int _nextEvent;
        private int _eventSourceRevision;
        private bool _isDispatching;

        private UITimelinePlayback(Channel[] channels, float duration, UITimelineEvent[] events,
            UITimeline eventSource = null, UIDisplay page = null, bool isClosingTransition = false) : base(duration)
        {
            _channels = channels;
            _events = events;
            _eventSource = eventSource;
            _page = page;
            _isClosingTransition = isClosingTransition;
            _eventSourceRevision = eventSource != null ? eventSource.EventSourceRevision : 0;
        }

        #region 公共方法

        internal static UITimelinePlayback Build(UITimelineAnimation animation, Transform root,
            UITimeline eventSource, UIDisplay page, bool isClosingTransition)
        {
            var channels = new Dictionary<UITimelineBinding, Channel>();
            foreach (UITimelineClip clip in animation.Clips)
            {
                if (clip == null || !ValidTime(clip.Duration))
                    throw new InvalidOperationException($"动画“{animation.Name}”包含无效片段时长。");
                GetChannel(clip.Binding).Spans.Add(clip.Span);
            }
            foreach (UITimelineKeyframeTrack track in animation.Tracks)
            {
                if (track == null || track.Keys.Count == 0)
                    throw new InvalidOperationException($"动画“{animation.Name}”的关键帧轨道至少需要一帧。");
                Channel channel = GetChannel(track.Binding);
                if (track.Keys.Any(key => key == null || !ValidTime(key.Time)))
                    throw new InvalidOperationException($"动画“{animation.Name}”的 {track.Binding} 包含无效关键帧。");
                var keys = track.Keys.OrderBy(key => key.Time).ToArray();
                if (keys.Length == 1)
                {
                    var key = keys[0];
                    channel.Spans.Add(new UITimelineSpan(key.Time, key.Time, key.Value, key.Value, key.Ease));
                }
                for (int i = 1; i < keys.Length; i++)
                {
                    var previous = keys[i - 1];
                    var current = keys[i];
                    if (previous.Time == current.Time)
                        throw new InvalidOperationException($"动画“{animation.Name}”的 {track.Binding} 在 {current.Time:0.###} 秒存在重复关键帧。");
                    channel.Spans.Add(new UITimelineSpan(previous.Time, current.Time, previous.Value, current.Value, previous.Ease));
                }
            }

            float duration = 0f;
            foreach (Channel channel in channels.Values)
            {
                channel.Spans.Sort((left, right) => left.Start.CompareTo(right.Start));
                for (int i = 0; i < channel.Spans.Count; i++)
                {
                    UITimelineSpan span = channel.Spans[i];
                    if (!ValidTime(span.Start) || !ValidTime(span.End))
                        throw new InvalidOperationException($"动画“{animation.Name}”的 {channel.Binding} 包含无效时间。");
                    if (i > 0)
                    {
                        UITimelineSpan previous = channel.Spans[i - 1];
                        if (span.Start < previous.End || span.Start == previous.Start)
                            throw new InvalidOperationException($"动画“{animation.Name}”的 {channel.Binding} 在 {span.Start:0.###} 秒出现重叠。");
                    }
                    duration = Mathf.Max(duration, span.End);
                }
            }
            UITimelineEvent[] events = BuildEvents(animation, ref duration);
            return new UITimelinePlayback(channels.Values.ToArray(), duration, events,
                eventSource, page, isClosingTransition);

            Channel GetChannel(UITimelineBinding binding)
            {
                if (binding.Target == null || !Enum.IsDefined(typeof(UITimelineProperty), binding.Property) ||
                    !UITimelineBinding.TargetType(binding.Property).IsInstanceOfType(binding.Target))
                    throw new InvalidOperationException($"动画“{animation.Name}”的 {binding} 缺少适用组件。");
                if (!binding.Target.transform.IsChildOf(root))
                    throw new InvalidOperationException($"动画“{animation.Name}”的 {binding} 超出所属页面或 Node 范围。");
                if (!channels.TryGetValue(binding, out Channel channel))
                    channels.Add(binding, channel = new Channel(binding));
                return channel;
            }
        }

        internal static UITimelinePlayback CreateTransition(CanvasGroup group, Transform scaledTarget,
            float fromAlpha, float toAlpha, Vector3 fromScale, Vector3 toScale, float duration, TweenEase ease)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (!ValidTime(duration)) throw new ArgumentOutOfRangeException(nameof(duration));
            bool hasScale = scaledTarget != null;
            var channels = new Channel[hasScale ? 2 : 1];
            channels[0] = new Channel(new UITimelineBinding(group, UITimelineProperty.Alpha));
            channels[0].Spans.Add(new UITimelineSpan(0f, duration,
                new Vector4(fromAlpha, 0f, 0f, 0f), new Vector4(toAlpha, 0f, 0f, 0f), ease));
            if (hasScale)
            {
                channels[1] = new Channel(new UITimelineBinding(scaledTarget, UITimelineProperty.Scale));
                channels[1].Spans.Add(new UITimelineSpan(0f, duration, fromScale, toScale, ease));
            }
            return new UITimelinePlayback(channels, duration, Array.Empty<UITimelineEvent>());
        }

        internal void Capture()
        {
            _snapshot = new Vector4[_channels.Length];
            for (int i = 0; i < _channels.Length; i++)
                _snapshot[i] = _channels[i].Binding.Read();
        }

        internal void Restore()
        {
            if (_snapshot == null) return;
            for (int i = 0; i < _channels.Length; i++)
                _channels[i].Binding.Write(_snapshot[i]);
            _snapshot = null;
        }

        #endregion

        #region 生命周期

        protected override void ApplyAt(float seconds)
        {
            foreach (Channel channel in _channels)
                channel.Binding.Write(channel.Evaluate(seconds));
        }

        protected override bool OnPlaybackSampled()
        {
            if (_isDispatching) return false;
            if (_nextEvent >= _events.Length || !Application.isPlaying) return true;

            // 同一个句柄可在回调里暂停、重播或跳转。旧播放在状态改变后立即交还调度器。
            int revision = PlaybackRevision;
            _isDispatching = true;
            try
            {
                while (_nextEvent < _events.Length && _events[_nextEvent].Time <= Elapsed && CanDispatch(revision))
                {
                    UITimelineEvent marker = _events[_nextEvent++];
                    foreach (Delegate subscriber in _eventSource.EventSubscribers)
                    {
                        if (!CanDispatch(revision)) break;
                        try { ((Action<UITimelineEvent>)subscriber)(marker); }
                        catch (Exception exception)
                        {
                            Log.Error($"[UI] 动画“{marker.AnimationName}”的事件“{marker.EventName}”回调失败。", exception);
                        }
                    }
                }
                return revision == PlaybackRevision && IsPlaying && !IsKilled && !IsComplete;
            }
            finally { _isDispatching = false; }
        }

        protected override void OnSilentSeek(float seconds)
        {
            // 跳转后只派发将来正常到达的事件，跳转位置自身也保持静默。
            int low = 0;
            int high = _events.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_events[middle].Time <= seconds) low = middle + 1;
                else high = middle;
            }
            _nextEvent = low;
        }

        protected override void ResetChildren()
        {
            _nextEvent = 0;
            _eventSourceRevision = _eventSource != null ? _eventSource.EventSourceRevision : 0;
        }

        #endregion

        #region 私有逻辑

        private bool CanDispatch(int revision)
        {
            if (revision != PlaybackRevision || !IsPlaying || IsKilled || IsComplete) return false;
            if (OwnerMissing || _eventSource == null || !_eventSource.isActiveAndEnabled ||
                _eventSource.EventSourceRevision != _eventSourceRevision ||
                (!_isClosingTransition && _page != null && _page.IsClosing))
            {
                _nextEvent = _events.Length;
                return false;
            }
            return true;
        }

        private static UITimelineEvent[] BuildEvents(UITimelineAnimation animation, ref float duration)
        {
            IReadOnlyList<UITimelineEventMarker> markers = animation.Events;
            if (markers.Count == 0) return Array.Empty<UITimelineEvent>();

            var events = new UITimelineEvent[markers.Count];
            for (int i = 0; i < markers.Count; i++)
            {
                UITimelineEventMarker marker = markers[i];
                if (marker == null || !ValidTime(marker.Time))
                    throw new InvalidOperationException($"动画“{animation.Name}”的第 {i + 1} 个事件时间无效。");
                if (string.IsNullOrWhiteSpace(marker.EventName))
                    throw new InvalidOperationException($"动画“{animation.Name}”的第 {i + 1} 个事件需要名称。");
                events[i] = new UITimelineEvent(animation.Name, marker.EventName, marker.Time);
                duration = Mathf.Max(duration, marker.Time);
            }
            // 保留同一时刻在资源中的声明顺序。
            return events.OrderBy(marker => marker.Time).ToArray();
        }

        private static bool ValidTime(float seconds) => seconds >= 0f && !float.IsInfinity(seconds);

        #endregion
    }
}
