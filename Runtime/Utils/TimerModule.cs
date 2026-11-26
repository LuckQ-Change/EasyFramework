using System;
using System.Collections.Generic;

namespace EasyFramework
{
    /// <summary>选择计时任务使用的帧时钟。</summary>
    public enum TimerTimeMode
    {
        Scaled,
        Unscaled,
    }

    /// <summary>根据所属宿主的时钟推进延迟和循环回调，回调内新增任务从下一帧开始。</summary>
    public class TimerModule : Module<TimerModule>, IFrameUpdate
    {
        /// <summary>保存一次计时任务的进度和时间模式。</summary>
        private sealed class TimerHandle
        {
            public int Id;
            public float Interval;
            public float Elapsed;
            public TimerTimeMode TimeMode;
            public bool IsRepeating;
            public Action Callback;
            public bool IsCanceled;
        }

        private readonly List<TimerHandle> _timers = new List<TimerHandle>();
        private readonly Queue<TimerHandle> _pending = new Queue<TimerHandle>();
        private int _nextId = 1;
        private bool _isTicking;

        #region 公共方法

        /// <summary>登记由此模块逐帧推进的延迟或循环任务。</summary>
        /// <param name="delay">首次触发和每次循环间隔的秒数，负数按零处理。</param>
        /// <param name="callback">计时到期时执行的回调。</param>
        /// <param name="isRepeating">到期后是否继续循环。</param>
        /// <param name="timeMode">推进任务所用的帧时钟。</param>
        /// <returns>用于取消任务的 ID，未提供回调时返回零。</returns>
        public int Schedule(float delay, Action callback, bool isRepeating = false,
            TimerTimeMode timeMode = TimerTimeMode.Scaled)
        {
            if (callback == null) return 0;
            if (float.IsNaN(delay) || float.IsInfinity(delay))
                throw new ArgumentOutOfRangeException(nameof(delay));
            if (timeMode != TimerTimeMode.Scaled && timeMode != TimerTimeMode.Unscaled)
                throw new ArgumentOutOfRangeException(nameof(timeMode));
            if (delay < 0f) delay = 0f;

            var handle = new TimerHandle
            {
                Id = _nextId++,
                Interval = delay,
                TimeMode = timeMode,
                IsRepeating = isRepeating,
                Callback = callback,
            };

            if (_isTicking) _pending.Enqueue(handle);
            else _timers.Add(handle);

            return handle.Id;
        }

        /// <summary>安排一次性延迟回调。</summary>
        /// <param name="seconds">触发前等待的秒数。</param>
        /// <param name="callback">等待结束时执行的回调。</param>
        /// <param name="timeMode">推进延迟所用的帧时钟。</param>
        /// <returns>用于取消任务的 ID，未提供回调时返回零。</returns>
        public int Delay(float seconds, Action callback, TimerTimeMode timeMode = TimerTimeMode.Scaled) =>
            Schedule(seconds, callback, false, timeMode);

        /// <summary>安排按间隔重复触发的回调。</summary>
        /// <param name="interval">每次触发之间的秒数。</param>
        /// <param name="callback">到达间隔时执行的回调。</param>
        /// <param name="timeMode">推进循环所用的帧时钟。</param>
        /// <returns>用于取消任务的 ID，未提供回调时返回零。</returns>
        public int Loop(float interval, Action callback, TimerTimeMode timeMode = TimerTimeMode.Scaled) =>
            Schedule(interval, callback, true, timeMode);

        /// <summary>取消当前宿主中指定的计时任务。</summary>
        /// <param name="id">登记时返回的任务 ID。</param>
        public void Cancel(int id)
        {
            if (id <= 0) return;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id == id)
                {
                    _timers[i].IsCanceled = true;
                    return;
                }
            }

            foreach (var timer in _pending)
            {
                if (timer.Id != id) continue;
                timer.IsCanceled = true;
                return;
            }
        }

        /// <summary>取消当前宿主已登记和正在排队的全部计时任务。</summary>
        public void CancelAll()
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                _timers[i].IsCanceled = true;
            }
            foreach (var timer in _pending) timer.IsCanceled = true;
        }

        #endregion

        #region 生命周期

        void IFrameUpdate.Tick(FrameTime frameTime)
        {
            if (_isTicking) return;
            _isTicking = true;
            try
            {
                for (int i = 0; i < _timers.Count; i++)
                {
                    TimerHandle timer = _timers[i];
                    if (timer.IsCanceled) continue;
                    float deltaTime = timer.TimeMode == TimerTimeMode.Unscaled
                        ? frameTime.UnscaledDeltaTime : frameTime.ScaledDeltaTime;
                    if (deltaTime <= 0f) continue;

                    timer.Elapsed += deltaTime;
                    if (timer.Elapsed < timer.Interval) continue;

                    try { timer.Callback(); }
                    catch (Exception exception) { Log.Error("[Timer] 计时回调执行失败。", exception); }

                    // 长帧只触发一次，保留不足一个周期的余量。
                    if (timer.IsRepeating) timer.Elapsed = timer.Interval > 0f ? timer.Elapsed % timer.Interval : 0f;
                    else timer.IsCanceled = true;
                }
            }
            finally
            {
                _isTicking = false;
                while (_pending.Count > 0)
                {
                    _timers.Add(_pending.Dequeue());
                }
                for (int i = _timers.Count - 1; i >= 0; i--)
                {
                    if (_timers[i].IsCanceled) _timers.RemoveAt(i);
                }
            }
        }

        protected override void OnShutdown()
        {
            _timers.Clear();
            _pending.Clear();
            _isTicking = false;
        }

        #endregion
    }
}
