using System;
using System.Collections.Generic;

namespace EasyFramework
{
    public class TimerModule : ModuleSingleton<TimerModule>
    {
        private class TimerHandle
        {
            public int Id;
            public float Interval;
            public float Elapsed;
            public bool Repeat;
            public Action Callback;
            public bool Canceled;
        }

        private readonly List<TimerHandle> _timers = new List<TimerHandle>();
        private readonly Queue<TimerHandle> _pending = new Queue<TimerHandle>();
        private int _nextId = 1;
        private bool _ticking;

        public int Schedule(float delay, Action callback, bool repeat = false)
        {
            if (callback == null) return 0;
            if (delay < 0f) delay = 0f;

            var handle = new TimerHandle
            {
                Id = _nextId++,
                Interval = delay,
                Elapsed = 0f,
                Repeat = repeat,
                Callback = callback,
            };

            if (_ticking) _pending.Enqueue(handle);
            else _timers.Add(handle);

            return handle.Id;
        }

        public int Delay(float seconds, Action callback) => Schedule(seconds, callback, false);

        public int Loop(float interval, Action callback) => Schedule(interval, callback, true);

        public void Cancel(int id)
        {
            if (id <= 0) return;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id == id)
                {
                    _timers[i].Canceled = true;
                    return;
                }
            }
        }

        public void CancelAll()
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                _timers[i].Canceled = true;
            }
        }

        protected override void OnUpdate(float deltaTime)
        {
            _ticking = true;
            for (int i = 0; i < _timers.Count; i++)
            {
                var t = _timers[i];
                if (t.Canceled) continue;

                t.Elapsed += deltaTime;
                if (t.Elapsed < t.Interval) continue;

                try
                {
                    t.Callback();
                }
                catch (Exception ex)
                {
                    Log.Error($"[Timer] callback error: {ex}");
                }

                if (t.Repeat)
                {
                    t.Elapsed -= t.Interval;
                }
                else
                {
                    t.Canceled = true;
                }
            }
            _ticking = false;

            while (_pending.Count > 0)
            {
                _timers.Add(_pending.Dequeue());
            }

            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                if (_timers[i].Canceled) _timers.RemoveAt(i);
            }
        }

        protected override void OnShutdown()
        {
            _timers.Clear();
            _pending.Clear();
        }
    }
}
