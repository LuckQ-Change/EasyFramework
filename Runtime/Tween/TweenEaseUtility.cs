using UnityEngine;

namespace EasyFramework.Tweening
{
    internal static class TweenEaseUtility
    {
        internal static float Evaluate(TweenEase ease, float progress)
        {
            float t = Mathf.Clamp01(progress);
            switch (ease)
            {
                case TweenEase.InQuad: return t * t;
                case TweenEase.OutQuad: return t * (2f - t);
                case TweenEase.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case TweenEase.InCubic: return t * t * t;
                case TweenEase.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case TweenEase.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case TweenEase.OutBack:
                    const float overshoot = 1.70158f;
                    return 1f + (overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
                default: return t;
            }
        }
    }
}
