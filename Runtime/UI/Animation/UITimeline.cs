using System;
using System.Collections.Generic;
using EasyFramework.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    /// <summary>标识时间轴片段作用的 UI 属性。</summary>
    public enum UITimelineProperty { Color, AnchoredPosition, Scale, Alpha }

    /// <summary>时间轴上的一个属性片段；起点与终点值显式保存以支持任意位置 Seek。</summary>
    [Serializable]
    public sealed class UITimelineClip
    {
        [SerializeField] private Component _target;
        [SerializeField] private UITimelineProperty _property;
        [SerializeField, Min(0f)] private float _start;
        [SerializeField, Min(0f)] private float _duration = 0.3f;
        [SerializeField] private TweenEase _ease = TweenEase.Linear;
        [SerializeField] private Color _fromColor = Color.white;
        [SerializeField] private Color _toColor = Color.white;
        [SerializeField] private Vector2 _fromPosition;
        [SerializeField] private Vector2 _toPosition;
        [SerializeField] private Vector3 _fromScale = Vector3.one;
        [SerializeField] private Vector3 _toScale = Vector3.one;
        [SerializeField, Range(0f, 1f)] private float _fromAlpha = 1f;
        [SerializeField, Range(0f, 1f)] private float _toAlpha = 1f;

        public Component Target => _target;
        public UITimelineProperty Property => _property;
        public float Start => _start;
        public float Duration => _duration;
        public TweenEase Ease => _ease;

        /// <summary>创建运行时和编辑器共用的属性 Tween；无效目标在此处给出定位错误。</summary>
        public TweenHandle CreateTween()
        {
            if (_target == null) throw new InvalidOperationException("UI 时间轴片段缺少目标组件。");
            TweenHandle tween;
            switch (_property)
            {
                case UITimelineProperty.Color when _target is Graphic graphic:
                    tween = TweenFactory.To(_fromColor, _toColor, _duration, value => graphic.color = value);
                    break;
                case UITimelineProperty.AnchoredPosition when _target is RectTransform rect:
                    tween = TweenFactory.To(_fromPosition, _toPosition, _duration,
                        value => rect.anchoredPosition = value);
                    break;
                case UITimelineProperty.Scale when _target is RectTransform scaleRect:
                    tween = TweenFactory.To(_fromScale, _toScale, _duration,
                        value => scaleRect.localScale = value);
                    break;
                case UITimelineProperty.Alpha when _target is CanvasGroup group:
                    tween = TweenFactory.To(_fromAlpha, _toAlpha, _duration, value => group.alpha = value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"UI 时间轴目标“{_target.name}”不支持 {_property} 属性。");
            }
            return tween.SetEase(_ease);
        }
    }

    /// <summary>按时间片段编排 UI 属性动画，编辑器 Seek 和运行时播放使用同一 TweenSequence。</summary>
    [AddComponentMenu("EasyFramework/UI/UI Timeline")]
    [DisallowMultipleComponent]
    public sealed class UITimeline : MonoBehaviour
    {
        [SerializeField] private List<UITimelineClip> _clips = new List<UITimelineClip>();
        [SerializeField] private TweenTimeMode _timeMode = TweenTimeMode.Unscaled;
        private TweenSequence _active;

        public IReadOnlyList<UITimelineClip> Clips => _clips;
        public float Duration
        {
            get
            {
                float end = 0f;
                foreach (UITimelineClip clip in _clips)
                    if (clip != null) end = Mathf.Max(end, clip.Start + clip.Duration);
                return end;
            }
        }

        /// <summary>构建一条独立 Sequence；调用方拥有它并负责播放、Seek 或 Kill。</summary>
        public TweenSequence BuildSequence()
        {
            TweenSequence sequence = TweenFactory.Sequence();
            foreach (UITimelineClip clip in _clips)
            {
                if (clip == null) continue;
                sequence.Insert(clip.Start, clip.CreateTween());
            }
            return sequence;
        }

        /// <summary>从起点播放，重复调用会先中断旧动画。</summary>
        public void Play()
        {
            Stop();
            _active = BuildSequence();
            _active.SetOwner(this).SetTimeMode(_timeMode).Play();
        }

        /// <summary>停止动画，不触发完成回调，也不强制写入终值。</summary>
        public void Stop()
        {
            _active?.Kill();
            _active = null;
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
