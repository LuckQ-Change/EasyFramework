using System;
using System.Collections.Generic;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework.UI
{
    public enum UITimelineProperty { Color, AnchoredPosition, Scale, Alpha, Rotation, FillAmount }

    [Serializable]
    public sealed class UITimelineClip
    {
        [SerializeField] private Component _target;
        [SerializeField] private UITimelineProperty _property = UITimelineProperty.AnchoredPosition;
        [SerializeField, Min(0f)] private float _start;
        [SerializeField, Min(0f)] private float _duration = 0.3f;
        [SerializeField] private TweenEase _ease;
        [SerializeField] private Vector4 _from;
        [SerializeField] private Vector4 _to;

        internal UITimelineBinding Binding => new(_target, _property);
        internal UITimelineSpan Span => new(_start, _start + _duration, _from, _to, _ease);
        internal float Duration => _duration;
    }

    [Serializable]
    public sealed class UITimelineKeyframe
    {
        [SerializeField, Min(0f)] private float _time;
        [SerializeField] private Vector4 _value;
        [SerializeField] private TweenEase _ease;

        internal float Time => _time;
        internal Vector4 Value => _value;
        internal TweenEase Ease => _ease;
    }

    [Serializable]
    public sealed class UITimelineKeyframeTrack
    {
        [SerializeField] private Component _target;
        [SerializeField] private UITimelineProperty _property = UITimelineProperty.AnchoredPosition;
        [SerializeField] private List<UITimelineKeyframe> _keys = new();

        internal UITimelineBinding Binding => new(_target, _property);
        internal IReadOnlyList<UITimelineKeyframe> Keys => _keys;
    }

    [Serializable]
    public sealed class UITimelineAnimation
    {
        [SerializeField] private string _name = "Animation";
        [SerializeField] private List<UITimelineClip> _clips = new();
        [SerializeField] private List<UITimelineKeyframeTrack> _tracks = new();

        public string Name => _name;
        internal IReadOnlyList<UITimelineClip> Clips => _clips;
        internal IReadOnlyList<UITimelineKeyframeTrack> Tracks => _tracks;
    }

    [AddComponentMenu("EasyFramework/UI/UI Timeline")]
    [DisallowMultipleComponent]
    public sealed class UITimeline : MonoBehaviour
    {
        [SerializeField] private List<UITimelineAnimation> _animations = new();
        [SerializeField] private TweenTimeMode _timeMode = TweenTimeMode.Unscaled;
        private UITimelinePlayback _active;

        public IReadOnlyList<UITimelineAnimation> Animations => _animations;

        public TweenHandle Play(string animation)
        {
            UITimelinePlayback playback = CreatePlayback(animation);
            Stop();
            _active = playback;
            return playback.Play();
        }

        public void Stop()
        {
            _active?.Kill();
            _active = null;
        }

        internal bool Contains(string animation) =>
            _animations.Exists(item => item != null && string.Equals(item.Name, animation, StringComparison.Ordinal));

        internal UITimelinePlayback CreatePlayback(string animation)
        {
            var data = _animations.Find(item => item != null && string.Equals(item.Name, animation, StringComparison.Ordinal));
            if (data == null) throw new InvalidOperationException($"UI 时间轴“{name}”找不到动画“{animation}”。");
            return CreatePlayback(data);
        }

        internal UITimelinePlayback CreatePlayback(UITimelineAnimation animation)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.Name))
                throw new InvalidOperationException("UI 时间轴动画需要名称。");
            UIBindingRoot root = GetComponentInParent<UIBindingRoot>(true);
            var playback = UITimelinePlayback.Build(animation, root != null ? root.transform : transform);
            playback.SetOwner(this).SetTimeMode(_timeMode);
            return playback;
        }

        internal void ValidateConfiguration()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (UITimelineAnimation animation in _animations)
            {
                if (animation == null || !names.Add(animation.Name))
                    throw new InvalidOperationException($"UI 时间轴“{name}”存在无效或重复的动画名称。");
                CreatePlayback(animation);
            }
        }

        private void OnDisable() => Stop();
    }
}
