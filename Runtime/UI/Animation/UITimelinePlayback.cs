using System;
using System.Collections.Generic;
using System.Linq;
using EasyFramework.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
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
                UITimelineProperty.Scale => ((RectTransform)Target).localScale,
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
                    ((RectTransform)Target).localScale = value;
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

        public bool Equals(UITimelineBinding other) => Target == other.Target && Property == other.Property;
        public override bool Equals(object obj) => obj is UITimelineBinding other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Target, Property);
        public override string ToString() => $"{(Target == null ? "未绑定" : Target.name)} · {Property}";
    }

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

    /// <summary>预览拖动和运行时播放都按绝对时间取样，每项属性只写入一次。</summary>
    internal sealed class UITimelinePlayback : TweenHandle
    {
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
        private Vector4[] _snapshot;

        private UITimelinePlayback(Channel[] channels, float duration) : base(duration) => _channels = channels;

        internal static UITimelinePlayback Build(UITimelineAnimation animation, Transform root)
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
            return new UITimelinePlayback(channels.Values.ToArray(), duration);

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

        protected override void ApplyAt(float seconds)
        {
            foreach (Channel channel in _channels)
                channel.Binding.Write(channel.Evaluate(seconds));
        }

        private static bool ValidTime(float seconds) => seconds >= 0f && !float.IsInfinity(seconds);
    }
}
