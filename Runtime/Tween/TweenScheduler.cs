using System;
using System.Collections.Generic;

namespace EasyFramework.Tweening
{
    /// <summary>统一推进运行中的 Tween；可由测试和编辑器手动调用。</summary>
    public static class TweenScheduler
    {
        private static readonly List<TweenHandle> Active = new List<TweenHandle>();
        private static readonly List<TweenHandle> Pending = new List<TweenHandle>();
        private static bool _updating;
        private static bool _shuttingDown;

        public static int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (TweenHandle tween in Active)
                    if (tween.IsPlaying && !tween.IsKilled && !tween.IsComplete) count++;
                foreach (TweenHandle tween in Pending)
                    if (tween.IsPlaying && !tween.IsKilled && !tween.IsComplete) count++;
                return count;
            }
        }

        internal static void Register(TweenHandle tween)
        {
            if (_shuttingDown) { tween.Kill(); return; }
            if (Active.Contains(tween) || Pending.Contains(tween)) return;
            if (_updating) Pending.Add(tween);
            else Active.Add(tween);
        }

        /// <summary>推进一次；运行时由 TweenModule 调用，编辑器和测试可手动调用。</summary>
        public static void Tick(float scaledDeltaTime, float unscaledDeltaTime)
        {
            if (_updating) throw new InvalidOperationException("TweenScheduler.Tick cannot be called recursively.");
            if (float.IsNaN(scaledDeltaTime) || float.IsInfinity(scaledDeltaTime) || scaledDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(scaledDeltaTime));
            if (float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime) || unscaledDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(unscaledDeltaTime));
            _updating = true;
            try
            {
                for (int i = 0; i < Active.Count; i++)
                {
                    TweenHandle tween = Active[i];
                    if (!tween.IsPlaying || tween.IsKilled || tween.IsComplete) continue;
                    try
                    {
                        tween.Tick(tween.TimeMode == TweenTimeMode.Unscaled
                            ? unscaledDeltaTime : scaledDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("[Tween] update failed.", exception);
                        tween.Kill();
                    }
                }
            }
            finally
            {
                _updating = false;
                int retained = 0;
                for (int i = 0; i < Active.Count; i++)
                {
                    TweenHandle tween = Active[i];
                    if (!tween.IsKilled && !tween.IsComplete) Active[retained++] = tween;
                }
                if (retained < Active.Count) Active.RemoveRange(retained, Active.Count - retained);
                foreach (TweenHandle tween in Pending)
                    if (!tween.IsKilled && !tween.IsComplete) Active.Add(tween);
                Pending.Clear();
            }
        }

        /// <summary>停止全部动画并释放调度记录，不派发完成回调。</summary>
        public static void KillAll()
        {
            _shuttingDown = true;
            try
            {
                foreach (TweenHandle tween in Active) tween.Kill();
                foreach (TweenHandle tween in Pending) tween.Kill();
                Active.Clear();
                Pending.Clear();
            }
            finally { _shuttingDown = false; }
        }
    }
}
