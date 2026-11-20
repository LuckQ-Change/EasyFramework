using System;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>将页面打开和关闭配置转换为 Tween；不承担 Tween 的逐帧调度。</summary>
    [AddComponentMenu("EasyFramework/UI/Transition")]
    [DisallowMultipleComponent]
    public sealed class UITransition : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _openDuration = 0.15f;
        [SerializeField, Min(0f)] private float _closeDuration = 0.12f;
        [SerializeField] private bool _scale = true;
        [SerializeField, Range(0.5f, 1f)] private float _startScale = 0.92f;
        [SerializeField] private UITimeline _openTimeline;
        [SerializeField] private string _openAnimation;
        [SerializeField] private UITimeline _closeTimeline;
        [SerializeField] private string _closeAnimation;

        private CanvasGroup _group;
        private TweenHandle _active;
        private UITimelinePlayback _openAppearance;
        private Vector3 _baseScale;
        private float _baseAlpha;
        private bool _baseBlocksRaycasts;
        private bool _captured;
        private bool _closing;
        private Action _closeCompletion;

        private void OnDisable()
        {
            StopCurrent();
            _openAppearance?.Restore();
            _openAppearance = null;
            if (_captured && _group != null)
            {
                _group.alpha = _baseAlpha;
                _group.blocksRaycasts = _baseBlocksRaycasts;
                transform.localScale = _baseScale;
            }
            CompleteCloseOnce();
        }

        public void PlayOpen()
        {
            if (_closing)
                throw new InvalidOperationException("A closing UI display cannot play its open transition again.");
            CaptureBaseAppearance();
            bool interrupted = _active != null && _active.IsPlaying;
            StopCurrent();
            gameObject.SetActive(true);
            if (_openTimeline != null)
            {
                UITimelinePlayback playback = _openTimeline.CreatePlayback(_openAnimation);
                if (_openAppearance == null)
                {
                    playback.Capture();
                    _openAppearance = playback;
                }
                _group.blocksRaycasts = false;
                PlayTween(playback, closing: false);
                return;
            }
            float fromAlpha = interrupted ? _group.alpha : 0f;
            Vector3 fromScale = interrupted ? transform.localScale : _baseScale * _startScale;
            _group.blocksRaycasts = false;

            var sequence = TweenFactory.Sequence();
            sequence.Append(TweenFactory.To(fromAlpha, _baseAlpha, _openDuration,
                value => _group.alpha = value).SetEase(TweenEase.OutCubic));
            if (_scale)
                sequence.Join(TweenFactory.To(fromScale, _baseScale, _openDuration,
                    value => transform.localScale = value).SetEase(TweenEase.OutCubic));
            PlayTween(sequence, closing: false);
        }

        public void PlayClose(Action completed)
        {
            if (_closing)
                throw new InvalidOperationException("The UI close transition is already running.");
            UITimelinePlayback playback = _closeTimeline != null ? _closeTimeline.CreatePlayback(_closeAnimation) : null;
            CaptureBaseAppearance();
            StopCurrent();
            _group.blocksRaycasts = false;
            _closing = true;
            _closeCompletion = completed;
            if (playback != null)
            {
                PlayTween(playback, closing: true);
                return;
            }

            var sequence = TweenFactory.Sequence();
            sequence.Append(TweenFactory.To(_group.alpha, 0f, _closeDuration,
                value => _group.alpha = value).SetEase(TweenEase.InCubic));
            if (_scale)
                sequence.Join(TweenFactory.To(transform.localScale, _baseScale * _startScale,
                    _closeDuration, value => transform.localScale = value).SetEase(TweenEase.InCubic));
            PlayTween(sequence, closing: true);
        }

        private void PlayTween(TweenHandle tween, bool closing)
        {
            _active = tween.SetTimeMode(TweenTimeMode.Unscaled).SetOwner(this).OnComplete(Finish).OnKill(Finish);
            if (Application.isPlaying && TweenModule.Instance != null) tween.Play();
            else tween.Complete();

            void Finish()
            {
                if (!ReferenceEquals(_active, tween)) return;
                _active = null;
                if (closing) CompleteCloseOnce();
                else _group.blocksRaycasts = _baseBlocksRaycasts;
            }
        }

        internal void ValidateTimelines()
        {
            Validate(_openTimeline, _openAnimation, "开场");
            Validate(_closeTimeline, _closeAnimation, "退场");

            void Validate(UITimeline timeline, string animation, string usage)
            {
                if (timeline == null && string.IsNullOrEmpty(animation)) return;
                if (timeline == null || !timeline.transform.IsChildOf(transform) || !timeline.Contains(animation))
                    throw new InvalidOperationException($"页面“{name}”的{usage}动画“{animation}”未正确配置。");
            }
        }

        private void CaptureBaseAppearance()
        {
            if (_captured) return;
            _group = GetComponent<CanvasGroup>();
            // UnityEngine.Object 有“已销毁但托管引用仍存在”的空值语义，不能用 ?? 判断。
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _baseScale = transform.localScale;
            _baseAlpha = _group.alpha;
            _baseBlocksRaycasts = _group.blocksRaycasts;
            _captured = true;
        }

        private void StopCurrent()
        {
            TweenHandle active = _active;
            _active = null;
            active?.Kill();
        }

        private void CompleteCloseOnce()
        {
            if (!_closing) return;
            _closing = false;
            Action completed = _closeCompletion;
            _closeCompletion = null;
            completed?.Invoke();
        }

    }
}
