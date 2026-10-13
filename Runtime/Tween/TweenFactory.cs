using System;
using UnityEngine;

namespace EasyFramework.Tweening
{
    /// <summary>创建常用值动画和组合动画，不管理播放状态。</summary>
    public static class TweenFactory
    {
        /// <summary>创建标量动画；调用 Play 后才进入调度器。</summary>
        public static TweenHandle To(float from, float to, float duration, Action<float> setter) =>
            new ValueTween<float>(from, to, duration, Mathf.LerpUnclamped, setter);

        /// <summary>创建二维向量动画。</summary>
        public static TweenHandle To(Vector2 from, Vector2 to, float duration, Action<Vector2> setter) =>
            new ValueTween<Vector2>(from, to, duration, Vector2.LerpUnclamped, setter);

        /// <summary>创建三维向量动画。</summary>
        public static TweenHandle To(Vector3 from, Vector3 to, float duration, Action<Vector3> setter) =>
            new ValueTween<Vector3>(from, to, duration, Vector3.LerpUnclamped, setter);

        /// <summary>创建颜色动画。</summary>
        public static TweenHandle To(Color from, Color to, float duration, Action<Color> setter) =>
            new ValueTween<Color>(from, to, duration, Color.LerpUnclamped, setter);

        /// <summary>创建空 Sequence；用 Append、Join 或 Insert 编排后播放。</summary>
        public static TweenSequence Sequence() => new TweenSequence();
    }
}
