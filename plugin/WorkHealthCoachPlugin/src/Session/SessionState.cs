namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    
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

        // ---- full-Keypad alerts (each has a start time and ends by itself or when cleared) ----
        public static DateTime EyeAlertStart { get; private set; }
        private static Boolean _eyeAlert;
        public static Boolean EyeAlertActive => _eyeAlert && (DateTime.UtcNow - EyeAlertStart).TotalSeconds < 90;   // until you blink, max 90 s

        public static DateTime PaceAlertStart { get; private set; }
        private static DateTime _paceAlertUntil = DateTime.MinValue;
        public static Boolean PaceAlertActive => DateTime.UtcNow < _paceAlertUntil;

        private static DateTime _timeAlertUntil = DateTime.MinValue;
        public static Boolean TimeAlertActive => DateTime.UtcNow < _timeAlertUntil;

        private static DateTime _behindUntil = DateTime.MinValue;
        public static Boolean BehindActive => DateTime.UtcNow < _behindUntil;

        public static Boolean TakeoverActive => EyeAlertActive || PaceAlertActive || TimeAlertActive || BehindActive;

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

            if (Running || AlertActive || PaceIsFast || TakeoverActive)
            {
                Raise();
            }
        }

        public static void DismissAlert()
        {
            LastTrigger = null;
            _eyeAlert = false;
            _paceAlertUntil = DateTime.MinValue;
            _timeAlertUntil = DateTime.MinValue;
            _behindUntil = DateTime.MinValue;
            Raise();
        }

        public static void SetMetrics(Double? wpm)
        {
            Wpm = wpm;
            Raise();
        }

        // Updates screen state from a trigger the policy let through.
        public static void OnTrigger(TriggerMessage m)
        {
            LastTrigger = m.Trigger;
            LastTriggerAt = DateTime.UtcNow;
            var now = DateTime.UtcNow;

            switch (m.Trigger)
            {
                case "pace_too_fast":
                    _paceFastUntil = now.AddSeconds(20);
                    if (!PaceAlertActive)
                    {
                        PaceAlertStart = now;
                    }

                    _paceAlertUntil = now.AddSeconds(15);
                    break;
                case "blink_low":
                    if (!EyeAlertActive)
                    {
                        EyeAlertStart = now;
                    }

                    _eyeAlert = true;
                    break;
                case "time_warning":
                    _timeAlertUntil = now.AddSeconds(10);
                    break;
                case "person_behind":
                    _behindUntil = now.AddSeconds(12);
                    break;
            }

            Raise();
        }

        // The user blinked: the eye reminder is done.
        public static void ClearBlinkAlert()
        {
            _eyeAlert = false;
            Raise();
        }

        public static void ClearPaceAlert()
        {
            _paceAlertUntil = DateTime.MinValue;
            Raise();
        }
    }
}
