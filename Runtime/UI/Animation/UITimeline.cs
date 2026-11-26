using System;
using System.Collections.Generic;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>时间轴可取样的 UI 属性。</summary>
    public enum UITimelineProperty { Color, AnchoredPosition, Scale, Alpha, Rotation, FillAmount }

    /// <summary>在一段持续时间内对单个 UI 属性进行插值。</summary>
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

    /// <summary>属性轨道上的一个时间和值。</summary>
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

    /// <summary>通过关键帧配置同一目标属性的连续变化。</summary>
    [Serializable]
    public sealed class UITimelineKeyframeTrack
    {
        [SerializeField] private Component _target;
        [SerializeField] private UITimelineProperty _property = UITimelineProperty.AnchoredPosition;
        [SerializeField] private List<UITimelineKeyframe> _keys = new();

        internal UITimelineBinding Binding => new(_target, _property);
        internal IReadOnlyList<UITimelineKeyframe> Keys => _keys;
    }

    /// <summary>动画独立事件轨上的一个命名标记。</summary>
    [Serializable]
    public sealed class UITimelineEventMarker
    {
        [SerializeField, Min(0f)] private float _time;
        [SerializeField] private string _eventName;

        /// <summary>事件相对动画起点的秒数。</summary>
        public float Time => _time;
        /// <summary>交给运行时订阅者的事件名称。</summary>
        public string EventName => _eventName;
    }

    /// <summary>描述一次自然播放跨过的事件帧。</summary>
    public readonly struct UITimelineEvent
    {
        /// <summary>触发事件的动画名称。</summary>
        public string AnimationName { get; }
        /// <summary>事件帧的名称。</summary>
        public string EventName { get; }
        /// <summary>事件帧在动画中的时间。</summary>
        public float Time { get; }

        internal UITimelineEvent(string animationName, string eventName, float time)
        {
            AnimationName = animationName;
            EventName = eventName;
            Time = time;
        }
    }

    /// <summary>保存一段具名动画的取样配置和事件标记。</summary>
    [Serializable]
    public sealed class UITimelineAnimation
    {
        [SerializeField] private string _name = "Animation";
        [SerializeField] private List<UITimelineClip> _clips = new();
        [SerializeField] private List<UITimelineKeyframeTrack> _tracks = new();
        [SerializeField] private List<UITimelineEventMarker> _events = new();

        /// <summary>调用播放时使用的动画名称。</summary>
        public string Name => _name;
        /// <summary>按资源声明顺序保存的事件标记。</summary>
        public IReadOnlyList<UITimelineEventMarker> Events => (IReadOnlyList<UITimelineEventMarker>)_events ?? Array.Empty<UITimelineEventMarker>();
        internal IReadOnlyList<UITimelineClip> Clips => _clips;
        internal IReadOnlyList<UITimelineKeyframeTrack> Tracks => _tracks;
    }

    /// <summary>播放 UI 属性动画并发布运行时事件帧。</summary>
    [AddComponentMenu("EasyFramework/UI/UI Timeline")]
    [DisallowMultipleComponent]
    public sealed class UITimeline : MonoBehaviour
    {
        [SerializeField] private List<UITimelineAnimation> _animations = new();
        [SerializeField] private TweenTimeMode _timeMode = TweenTimeMode.Unscaled;
        private UITimelinePlayback _active;
        private Action<UITimelineEvent> _eventTriggered;
        private Delegate[] _eventSubscribers = Array.Empty<Delegate>();
        private int _eventSourceRevision;

        /// <summary>此组件可播放的具名动画。</summary>
        public IReadOnlyList<UITimelineAnimation> Animations => _animations;
        /// <summary>正常播放到达事件帧时触发。编辑器预览、跳转和强制完成保持静默。</summary>
        public event Action<UITimelineEvent> EventTriggered
        {
            add
            {
                _eventTriggered += value;
                RefreshEventSubscribers();
            }
            remove
            {
                _eventTriggered -= value;
                RefreshEventSubscribers();
            }
        }

        internal Delegate[] EventSubscribers => _eventSubscribers;
        internal int EventSourceRevision => _eventSourceRevision;

        #region 公共方法

        /// <summary>从头播放一段动画，替换当前的手动播放。</summary>
        /// <param name="animation">组件内配置的动画名称。</param>
        /// <returns>用于暂停、继续或停止的动画句柄。</returns>
        public TweenHandle Play(string animation)
        {
            UITimelinePlayback playback = CreatePlayback(animation);
            UITimelinePlayback previous = _active;
            _active = playback;
            // 先登记本次播放，让停止回调重入时能正确找到并替换它。
            previous?.Kill();
            return playback.Play();
        }

        /// <summary>停止当前的手动播放和它剩余的事件派发。</summary>
        public void Stop()
        {
            UITimelinePlayback playback = _active;
            _active = null;
            playback?.Kill();
        }

        #endregion

        #region 私有逻辑

        private void RefreshEventSubscribers()
        {
            // 订阅变化时生成快照，逐帧派发使用现有数组。
            _eventSubscribers = _eventTriggered?.GetInvocationList() ?? Array.Empty<Delegate>();
        }

        internal bool Contains(string animation) =>
            _animations.Exists(item => item != null && string.Equals(item.Name, animation, StringComparison.Ordinal));

        internal UITimelinePlayback CreatePlayback(string animation, bool isClosingTransition = false)
        {
            var data = _animations.Find(item => item != null && string.Equals(item.Name, animation, StringComparison.Ordinal));
            if (data == null) throw new InvalidOperationException($"UI 时间轴“{name}”找不到动画“{animation}”。");
            return CreatePlayback(data, isClosingTransition);
        }

        internal UITimelinePlayback CreatePlayback(UITimelineAnimation animation, bool isClosingTransition = false)
        {
            if (animation == null || string.IsNullOrWhiteSpace(animation.Name))
                throw new InvalidOperationException("UI 时间轴动画需要名称。");
            UIBindingRoot root = GetComponentInParent<UIBindingRoot>(true);
            UIDisplay page = animation.Events.Count == 0 ? null : FindOwningPage();
            var playback = UITimelinePlayback.Build(animation, root != null ? root.transform : transform,
                this, page, isClosingTransition);
            playback.SetOwner(this).SetTimeMode(_timeMode);
            return playback;
        }

        private UIDisplay FindOwningPage()
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (current.TryGetComponent(out UIDisplay page) && page.IsPage)
                    return page;
            return null;
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

        #endregion

        #region 生命周期

        private void OnDisable()
        {
            _eventSourceRevision++;
            Stop();
        }

        #endregion
    }
}
