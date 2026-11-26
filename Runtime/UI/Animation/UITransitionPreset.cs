using System;
using EasyFramework.Tweening;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>保存可由多份页面 Prefab 共用的进退场参数。</summary>
    [CreateAssetMenu(menuName = "EasyFramework/UI/Transition Preset", fileName = "UITransitionPreset")]
    public sealed class UITransitionPreset : ScriptableObject
    {
        private const float DEFAULT_OPEN_DURATION = 0.15f;
        private const float DEFAULT_CLOSE_DURATION = 0.12f;
        private const float DEFAULT_SCALE = 0.92f;

        [Header("进场")]
        [SerializeField, Min(0f)] private float _openDuration = DEFAULT_OPEN_DURATION;
        [SerializeField] private TweenEase _openEase = TweenEase.OutCubic;
        [SerializeField, Range(0f, 1f), Tooltip("起始透明度相对于页面原始透明度的比例。")]
        private float _openAlpha;
        [SerializeField] private bool _hasOpenScale = true;
        [SerializeField, Min(0f), Tooltip("启用进场缩放时使用，表示相对于页面原始缩放的起始倍率。")]
        private float _openScale = DEFAULT_SCALE;

        [Header("退场")]
        [SerializeField, Min(0f)] private float _closeDuration = DEFAULT_CLOSE_DURATION;
        [SerializeField] private TweenEase _closeEase = TweenEase.InCubic;
        [SerializeField, Range(0f, 1f), Tooltip("结束透明度相对于页面原始透明度的比例。")]
        private float _closeAlpha;
        [SerializeField] private bool _hasCloseScale = true;
        [SerializeField, Min(0f), Tooltip("启用退场缩放时使用，表示相对于页面原始缩放的结束倍率。")]
        private float _closeScale = DEFAULT_SCALE;

        internal UITransitionMotion GetMotion(bool isClosing)
        {
            UITransitionMotion motion = isClosing
                ? new UITransitionMotion(_closeDuration, _closeEase, _closeAlpha, _hasCloseScale, _closeScale)
                : new UITransitionMotion(_openDuration, _openEase, _openAlpha, _hasOpenScale, _openScale);
            string issue = motion.GetValidationIssue();
            if (issue != null)
            {
                string phase = isClosing ? "退场" : "进场";
                throw new InvalidOperationException($"转场预设“{name}”的{phase}{issue}");
            }
            return motion;
        }

        internal static UITransitionMotion DefaultMotion(bool isClosing) => isClosing
            ? new UITransitionMotion(DEFAULT_CLOSE_DURATION, TweenEase.InCubic, 0f, true, DEFAULT_SCALE)
            : new UITransitionMotion(DEFAULT_OPEN_DURATION, TweenEase.OutCubic, 0f, true, DEFAULT_SCALE);
    }

    /// <summary>提供单次进场或退场所需的时长、曲线和相对端点。</summary>
    internal readonly struct UITransitionMotion
    {
        internal UITransitionMotion(float duration, TweenEase ease, float alpha, bool hasScale, float scale)
        {
            Duration = duration;
            Ease = ease;
            Alpha = alpha;
            HasScale = hasScale;
            Scale = scale;
        }

        internal float Duration { get; }
        internal TweenEase Ease { get; }
        internal float Alpha { get; }
        internal bool HasScale { get; }
        internal float Scale { get; }

        internal string GetValidationIssue()
        {
            if (!IsFinite(Duration) || Duration < 0f) return "时长必须是非负有限数。";
            if (!Enum.IsDefined(typeof(TweenEase), Ease)) return "缓动曲线无效。";
            if (!IsFinite(Alpha) || Alpha < 0f || Alpha > 1f) return "透明度比例必须在 0 到 1 之间。";
            if (HasScale && (!IsFinite(Scale) || Scale < 0f)) return "缩放倍率必须是非负有限数。";
            return null;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
