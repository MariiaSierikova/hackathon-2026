namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Threading.Tasks;

    // Shared state behind the Keypad widgets: presentation timers, pace, blink animation, last alert.
    public static class SessionState
    {
        private static readonly Object Gate = new();

        // ---- settings (will move to the settings file / page) ----
        public static Int32 TotalMinutes { get; set; } = 20;
        public static Int32 WarnMinutes { get; set; } = 1;   // "1 minute left" haptic

        // ---- timers ----
        public static Boolean Running { get; private set; }
        private static DateTime _startedAt;
        private static TimeSpan _elapsedBeforePause = TimeSpan.Zero;
        private static DateTime _slideStartedAt = DateTime.UtcNow;
        private static Boolean _warned;

        public static Int32 SlideNumber { get; private set; } = 1;

        public static TimeSpan Elapsed => Running ? _elapsedBeforePause + (DateTime.UtcNow - _startedAt) : _elapsedBeforePause;
        public static TimeSpan Remaining => TimeSpan.FromMinutes(TotalMinutes) - Elapsed;
        public static TimeSpan SlideElapsed => Running ? DateTime.UtcNow - _slideStartedAt : TimeSpan.Zero;

        // ---- pace ----
        public static Double? Wpm { get; private set; }
        private static DateTime _paceFastUntil = DateTime.MinValue;
        public static Boolean PaceIsFast => DateTime.UtcNow < _paceFastUntil;

        // ---- eye ----
        public static Double EyeOpenness { get; private set; } = 1.0;   // 1 open, 0 closed
        public static Boolean BlinkReminderActive { get; private set; }

        // ---- last alert ----
        public static String LastTrigger { get; private set; }
        public static DateTime LastTriggerAt { get; private set; } = DateTime.MinValue;
        public static Boolean AlertActive => LastTrigger != null && (DateTime.UtcNow - LastTriggerAt).TotalSeconds < 12;

        // Raised whenever any widget should redraw.
        public static event Action Changed;

        private static void Raise() => Changed?.Invoke();

        public static void StartPause()
        {
            lock (Gate)
            {
                if (Running)
                {
                    _elapsedBeforePause += DateTime.UtcNow - _startedAt;
                    Running = false;
                }
                else
                {
                    _startedAt = DateTime.UtcNow;
                    if (_elapsedBeforePause == TimeSpan.Zero)
                    {
                        _slideStartedAt = _startedAt;
                    }

                    Running = true;
                }
            }

            Raise();
        }

        public static void Reset()
        {
            lock (Gate)
            {
                Running = false;
                _elapsedBeforePause = TimeSpan.Zero;
                _warned = false;
                SlideNumber = 1;
                _slideStartedAt = DateTime.UtcNow;
                LastTrigger = null;
            }

            Raise();
        }

        public static void NextSlide()
        {
            lock (Gate)
            {
                SlideNumber++;
                _slideStartedAt = DateTime.UtcNow;
            }

            Raise();
        }

        public static void PrevSlide()
        {
            lock (Gate)
            {
                if (SlideNumber > 1)
                {
                    SlideNumber--;
                }

                _slideStartedAt = DateTime.UtcNow;
            }

            Raise();
        }

        // Called once a second by the plugin.
        public static void Tick()
        {
            Boolean fireWarning = false;
            lock (Gate)
            {
                if (Running && !_warned && Remaining <= TimeSpan.FromMinutes(WarnMinutes))
                {
                    _warned = true;
                    fireWarning = true;
                }
            }

            if (fireWarning)
            {
                TriggerBus.Publish(new TriggerMessage("time_warning", 1.0));
            }

            if (Running || AlertActive || PaceIsFast)
            {
                Raise();
            }
        }

        public static void SetMetrics(Double? wpm)
        {
            Wpm = wpm;
            Raise();
        }

        // Updates widget state from any trigger.
        public static void OnTrigger(TriggerMessage m)
        {
            LastTrigger = m.Trigger;
            LastTriggerAt = DateTime.UtcNow;

            if (m.Trigger == "pace_too_fast")
            {
                _paceFastUntil = DateTime.UtcNow.AddSeconds(20);
            }
            else if (m.Trigger == "blink_low")
            {
                _ = AnimateBlinkAsync();
            }

            Raise();
        }

        // Two quick blinks, then keeps a "blink" hint for a few seconds.
        private static async Task AnimateBlinkAsync()
        {
            try
            {
                BlinkReminderActive = true;
                Double[] frames = { 1.0, 0.5, 0.0, 0.5, 1.0, 1.0, 0.5, 0.0, 0.5, 1.0 };
                foreach (var f in frames)
                {
                    EyeOpenness = f;
                    Raise();
                    await Task.Delay(130);
                }

                await Task.Delay(2500);
            }
            finally
            {
                EyeOpenness = 1.0;
                BlinkReminderActive = false;
                Raise();
            }
        }
    }
}
