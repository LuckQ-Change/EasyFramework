using System;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>将页面开关、视觉基准和输入恢复接入统一的时间轴播放。</summary>
    [AddComponentMenu("EasyFramework/UI/Transition")]
    [DisallowMultipleComponent]
    public sealed class UITransition : MonoBehaviour
    {
        [SerializeField] private UITransitionPreset _preset;
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

        #region 生命周期

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

        #endregion

        #region 公共方法

        /// <summary>播放进场动画，结束时还原页面输入设置。</summary>
        public void PlayOpen()
        {
            if (_closing)
                throw new InvalidOperationException("A closing UI display cannot play its open transition again.");
            CaptureBaseAppearance();
            bool isInterrupted = _active != null && _active.IsPlaying;
            UITimelinePlayback playback;
            if (_openTimeline != null)
            {
                playback = _openTimeline.CreatePlayback(_openAnimation);
            }
            else
            {
                UITransitionMotion motion = GetMotion(isClosing: false);
                // 首次进场按页面的基准缩放取起点。重复进场从当前采样值接续。
                float fromAlpha = isInterrupted ? _group.alpha : _baseAlpha * motion.Alpha;
                Vector3 fromScale = isInterrupted ? transform.localScale : _baseScale * motion.Scale;
                playback = UITimelinePlayback.CreateTransition(_group, motion.HasScale ? transform : null,
                    fromAlpha, _baseAlpha, fromScale, _baseScale, motion.Duration, motion.Ease);
            }

            StopCurrent();
            gameObject.SetActive(true);
            if (_openTimeline != null && _openAppearance == null)
            {
                playback.Capture();
                _openAppearance = playback;
            }
            _group.blocksRaycasts = false;
            PlayTween(playback, closing: false);
        }

        /// <summary>播放退场动画，结束时交还页面管理器收尾。</summary>
        /// <param name="completed">用于完成关闭的回调。</param>
        public void PlayClose(Action completed)
        {
            if (_closing)
                throw new InvalidOperationException("The UI close transition is already running.");
            CaptureBaseAppearance();
            UITimelinePlayback playback;
            if (_closeTimeline != null)
            {
                playback = _closeTimeline.CreatePlayback(_closeAnimation, isClosingTransition: true);
            }
            else
            {
                UITransitionMotion motion = GetMotion(isClosing: true);
                // 从当前视觉状态退场，使进场中途关闭时能连续播放。
                playback = UITimelinePlayback.CreateTransition(_group, motion.HasScale ? transform : null,
                    _group.alpha, _baseAlpha * motion.Alpha, transform.localScale, _baseScale * motion.Scale,
                    motion.Duration, motion.Ease);
            }
            StopCurrent();
            _group.blocksRaycasts = false;
            _closing = true;
            _closeCompletion = completed;
            PlayTween(playback, closing: true);
        }

        #endregion

        #region 私有逻辑

        private UITransitionMotion GetMotion(bool isClosing)
        {
            if (_preset != null) return _preset.GetMotion(isClosing);
            return UITransitionPreset.DefaultMotion(isClosing);
        }

        private void PlayTween(TweenHandle tween, bool closing)
        {
            _active = tween.SetTimeMode(TweenTimeMode.Unscaled).SetOwner(this).OnComplete(Finish).OnKill(Finish);
            try
            {
                if (Application.isPlaying && TweenModule.Instance != null) tween.Play();
                else tween.Complete();
            }
            catch
            {
                tween.Kill();
                throw;
            }

            void Finish()
            {
                if (!ReferenceEquals(_active, tween)) return;
                _active = null;
                if (closing) CompleteCloseOnce();
                else _group.blocksRaycasts = _baseBlocksRaycasts;
            }
        }

        internal void ValidateConfiguration()
        {
            Validate(_openTimeline, _openAnimation, "开场");
            Validate(_closeTimeline, _closeAnimation, "退场");
            if (_preset != null && _openTimeline == null) _preset.GetMotion(isClosing: false);
            if (_preset != null && _closeTimeline == null) _preset.GetMotion(isClosing: true);

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

        #endregion
    }
}
