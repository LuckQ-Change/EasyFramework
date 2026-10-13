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

        private CanvasGroup _group;
        private TweenHandle _active;
        private Vector3 _baseScale;
        private float _baseAlpha;
        private bool _baseBlocksRaycasts;
        private bool _captured;
        private bool _closing;
        private Action _closeCompletion;

        private void OnDisable()
        {
            StopCurrent();
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
            float fromAlpha = interrupted ? _group.alpha : 0f;
            Vector3 fromScale = interrupted ? transform.localScale : _baseScale * _startScale;
            _group.blocksRaycasts = false;

            var sequence = TweenFactory.Sequence();
            sequence.Append(TweenFactory.To(fromAlpha, _baseAlpha, _openDuration,
                value => _group.alpha = value).SetEase(TweenEase.OutCubic));
            if (_scale)
                sequence.Join(TweenFactory.To(fromScale, _baseScale, _openDuration,
                    value => transform.localScale = value).SetEase(TweenEase.OutCubic));
            sequence.SetOwner(this).SetTimeMode(TweenTimeMode.Unscaled)
                .OnComplete(() => { _active = null; _group.blocksRaycasts = _baseBlocksRaycasts; });
            _active = sequence;
            Run(sequence);
        }

        public void PlayClose(Action completed)
        {
            if (_closing)
                throw new InvalidOperationException("The UI close transition is already running.");
            CaptureBaseAppearance();
            StopCurrent();
            _group.blocksRaycasts = false;
            _closing = true;
            _closeCompletion = completed;

            var sequence = TweenFactory.Sequence();
            sequence.Append(TweenFactory.To(_group.alpha, 0f, _closeDuration,
                value => _group.alpha = value).SetEase(TweenEase.InCubic));
            if (_scale)
                sequence.Join(TweenFactory.To(transform.localScale, _baseScale * _startScale,
                    _closeDuration, value => transform.localScale = value).SetEase(TweenEase.InCubic));
            sequence.SetOwner(this).SetTimeMode(TweenTimeMode.Unscaled)
                .OnComplete(() => { _active = null; CompleteCloseOnce(); });
            _active = sequence;
            Run(sequence);
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
            _active?.Kill();
            _active = null;
        }

        private void CompleteCloseOnce()
        {
            if (!_closing) return;
            _closing = false;
            Action completed = _closeCompletion;
            _closeCompletion = null;
            completed?.Invoke();
        }

        private static void Run(TweenHandle tween)
        {
            // 直接使用 UIManager 时可能没有 FrameworkLauncher；此时同步写入终值，避免页面卡住。
            if (Application.isPlaying && TweenModule.Instance != null) tween.Play();
            else tween.Complete();
        }
    }
}
