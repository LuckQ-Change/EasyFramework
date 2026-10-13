using System;

namespace EasyFramework.Tweening
{
    /// <summary>只负责对一种值插值并写入目标。</summary>
    internal sealed class ValueTween<T> : TweenHandle
    {
        private readonly T _from;
        private readonly T _to;
        private readonly Func<T, T, float, T> _lerp;
        private readonly Action<T> _setter;

        internal ValueTween(T from, T to, float duration, Func<T, T, float, T> lerp, Action<T> setter)
            : base(duration)
        {
            _from = from;
            _to = to;
            _lerp = lerp ?? throw new ArgumentNullException(nameof(lerp));
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        }

        protected override void ApplyAt(float seconds) => _setter(_lerp(_from, _to, EaseAt(seconds)));
    }
}
