namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Globalization;

    // Camera presence, shared by everything that cares whether someone sits in front of the screen.
    public static class Presence
    {
        public static void Set(Boolean atDesk)
        {
            PomodoroState.SetPresence(atDesk);
            FarVolume.SetPresence(atDesk);
        }
    }

    // "Volume when far": keeps the sound audible when the user moves away from the screen.
    //   far      = the face is clearly smaller than at the normal distance (ratio <= 1 - shrink%) or out of the frame,
    //              for at least HoldSec seconds
    //   far      -> raise the system volume by BoostPct (never above CapPct), gradually if Smooth
    //   back     -> restore the volume that was set before (if Restore); if the user changed the volume by hand meanwhile, leave it alone
    //   away for LeaveSec seconds -> stop the sound (mute, or press Play/Pause); coming back undoes it (if Resume)
    // Inputs need NO camera access here: any detector (or the settings page's simulator) sends
    //   presence: /signal/presence/<bool>        distance: POST /distance {"ratio":0.6} or {"face":0.11} after /distance/calibrate
    public static class FarVolume
    {
        private static readonly Object Gate = new();

        // ---- settings (from the settings page, see UiSettings) ----
        public static Boolean Enabled { get; private set; }

        public static Int32 ShrinkPct { get; private set; } = 30;

        public static Double HoldSec { get; private set; } = 2;

        public static Int32 BoostPct { get; private set; } = 20;

        public static Int32 CapPct { get; private set; } = 90;

        public static Boolean Smooth { get; private set; } = true;

        public static Boolean Restore { get; private set; } = true;

        public static Int32 LeaveSec { get; private set; } = 120;

        public static Boolean Resume { get; private set; } = true;

        public static Boolean UsePlayPause { get; private set; }

        // ---- wiring (replaced by tests) ----
        public static ISystemAudio Audio = new NoAudio();
        public static Func<DateTime> Now = () => DateTime.UtcNow;
        public static Action SendPlayPause;

        // ---- inputs ----
        public static Boolean AtDesk { get; private set; } = true;

        public static Double? Baseline { get; private set; }

        public static Double? LastFace { get; private set; }

        private static Double? _ratio;
        private static DateTime _ratioAt = DateTime.MinValue;
        private static DateTime? _farSince;
        private static DateTime? _nearSince;
        private static DateTime? _awaySince;
        private static DateTime _retryAt = DateTime.MinValue;
        private static Boolean _suppress;   // the user took over the volume: no new boost until they are near again

        // ---- state ----
        public static Boolean Boosted { get; private set; }

        public static Boolean Stopped { get; private set; }

        private static Int32? _original;
        private static Int32? _lastSet;
        private static Int32? _rampTarget;
        private static Int32 _boostTarget;
        private static Boolean _restoring;
        private static Boolean? _wasMuted;
        private static Boolean _pausedByKey;

        public static void Configure(Boolean enabled, Int32 shrinkPct, Double holdSec, Int32 boostPct, Int32 capPct, Boolean smooth,
            Boolean restore, Int32 leaveSec, Boolean resume, Boolean usePlayPause)
        {
            lock (Gate)
            {
                ShrinkPct = Math.Clamp(shrinkPct, 5, 90);
                HoldSec = Math.Clamp(holdSec, 0.5, 30);
                BoostPct = Math.Clamp(boostPct, 1, 80);
                CapPct = Math.Clamp(capPct, 20, 100);
                Smooth = smooth;
                Restore = restore;
                LeaveSec = Math.Max(0, leaveSec);
                Resume = resume;
                UsePlayPause = usePlayPause;
                var was = Enabled;
                Enabled = enabled;
                if (was && !enabled)
                {
                    ReleaseLocked();
                }
            }
        }

        // ---- inputs ----
        public static void SetPresence(Boolean atDesk)
        {
            lock (Gate)
            {
                if (atDesk == AtDesk)
                {
                    return;
                }

                AtDesk = atDesk;
                _awaySince = atDesk ? null : Now();
                if (atDesk)
                {
                    _ratio = null;   // wait for a fresh distance sample
                }
            }
        }

        // Face size relative to the normal distance: 1.0 = normal, 0.6 = 40 % smaller (further away).
        public static void SetRatio(Double ratio)
        {
            lock (Gate)
            {
                _ratio = Math.Clamp(ratio, 0.0, 5.0);
                _ratioAt = Now();
            }
        }

        // Raw face width (any unit, e.g. fraction of the frame). Needs a baseline from Calibrate().
        public static Boolean SetFace(Double face)
        {
            lock (Gate)
            {
                LastFace = face;
                if (Baseline is not > 0)
                {
                    return false;
                }

                _ratio = Math.Clamp(face / Baseline.Value, 0.0, 5.0);
                _ratioAt = Now();
                return true;
            }
        }

        // "This is my normal distance": takes the given face width or the latest one.
        public static Boolean Calibrate(Double? face)
        {
            lock (Gate)
            {
                var f = face ?? LastFace;
                if (f is not > 0)
                {
                    return false;
                }

                Baseline = f;
                LastFace = f;
                _ratio = 1.0;
                _ratioAt = Now();
                return true;
            }
        }

        // Called once a second by the plugin (tests can pass more seconds).
        public static void Tick(Int32 seconds = 1)
        {
            var publish = false;
            lock (Gate)
            {
                if (!Enabled)
                {
                    return;
                }

                var now = Now();
                if (_ratio.HasValue && now - _ratioAt > TimeSpan.FromSeconds(5))
                {
                    _ratio = null;   // no fresh sample: do not stay "far" on old data
                }

                var away = !AtDesk;
                var small = !away && _ratio.HasValue && _ratio.Value <= 1.0 - ShrinkPct / 100.0;
                var far = away || small;
                if (far)
                {
                    _nearSince = null;
                    _farSince ??= now;
                }
                else
                {
                    _farSince = null;
                    _nearSince ??= now;
                    _suppress = false;
                }

                // the user turned the volume themselves while it was raised: do not fight them
                if (Boosted && _lastSet.HasValue && Audio.GetVolume() is { } actual && Math.Abs(actual - _lastSet.Value) > 3)
                {
                    PluginLog.Info($"FarVolume: volume changed by the user ({_lastSet} -> {actual}), leaving it alone");
                    ForgetBoostLocked();
                    _suppress = true;
                }

                if (far && !Boosted && !_suppress && now >= _retryAt && now - _farSince.Value >= TimeSpan.FromSeconds(HoldSec))
                {
                    publish = StartBoostLocked(now);
                }
                else if (far && Boosted && _restoring)
                {
                    _restoring = false;   // they moved away again before the volume was back
                    _rampTarget = _boostTarget;
                }
                else if (!far && Boosted && !_restoring && now - _nearSince.Value >= TimeSpan.FromSeconds(Math.Min(HoldSec, 2)))
                {
                    BeginRestoreLocked();
                }

                if (away)
                {
                    if (LeaveSec > 0 && !Stopped && _awaySince.HasValue && now - _awaySince.Value >= TimeSpan.FromSeconds(LeaveSec))
                    {
                        StopLocked();
                    }
                }
                else if (Stopped)
                {
                    ResumeLocked();
                }

                RampLocked(seconds);
            }

            if (publish)
            {
                TriggerBus.Publish(new TriggerMessage("volume_far", 1.0));
            }
        }

        // Feature switched off / plugin unloading: give the user their sound and volume back.
        public static void Release()
        {
            lock (Gate)
            {
                ReleaseLocked();
            }
        }

        // ---- internals (call with the lock held) ----
        private static Boolean StartBoostLocked(DateTime now)
        {
            var cur = Audio.GetVolume();
            if (!cur.HasValue)
            {
                _retryAt = now + TimeSpan.FromSeconds(10);   // no readable volume (no device / no permission): try again later
                PluginLog.Warning($"FarVolume: cannot read the volume ({Audio.Name}), retrying in 10 s");
                return false;
            }

            _original = cur;
            _boostTarget = Math.Max(cur.Value, Math.Min(CapPct, cur.Value + BoostPct));   // never lower it, never above the cap
            _lastSet = cur;
            _rampTarget = _boostTarget;
            _restoring = false;
            Boosted = true;
            PluginLog.Info($"FarVolume: far from the screen, volume {cur}% -> {_boostTarget}% ({Audio.Name})");
            return _boostTarget > cur.Value;
        }

        private static void BeginRestoreLocked()
        {
            if (!Restore || !_original.HasValue)
            {
                ForgetBoostLocked();
                return;
            }

            _restoring = true;
            _rampTarget = _original;
            PluginLog.Info($"FarVolume: back near the screen, restoring volume to {_original}%");
        }

        private static void ForgetBoostLocked()
        {
            Boosted = false;
            _restoring = false;
            _rampTarget = null;
            _original = null;
            _lastSet = null;
        }

        private static void RampLocked(Int32 seconds)
        {
            if (!_rampTarget.HasValue)
            {
                return;
            }

            var target = _rampTarget.Value;
            var cur = _lastSet ?? Audio.GetVolume() ?? target;
            var step = Smooth ? Math.Max(1, 5 * seconds) : 100;
            var next = cur < target ? Math.Min(target, cur + step) : Math.Max(target, cur - step);
            if (next != cur)
            {
                if (!Audio.SetVolume(next))
                {
                    PluginLog.Warning("FarVolume: could not set the volume");
                    ForgetBoostLocked();
                    return;
                }

                _lastSet = next;
            }

            if (next == target)
            {
                _rampTarget = null;
                if (_restoring)
                {
                    ForgetBoostLocked();
                }
            }
        }

        private static void StopLocked()
        {
            Stopped = true;
            if (UsePlayPause)
            {
                SendPlayPause?.Invoke();
                _pausedByKey = true;
                PluginLog.Info("FarVolume: away, sent Play/Pause");
            }
            else
            {
                _wasMuted = Audio.GetMuted();
                if (_wasMuted != true)
                {
                    Audio.SetMuted(true);
                }

                PluginLog.Info("FarVolume: away, sound muted");
            }
        }

        private static void ResumeLocked(Boolean force = false)
        {
            Stopped = false;
            if (_pausedByKey)
            {
                _pausedByKey = false;
                if (Resume || force)
                {
                    SendPlayPause?.Invoke();
                }
            }
            else if (_wasMuted != true && (Resume || force))
            {
                Audio.SetMuted(false);   // we muted it, so we unmute it; if the user had it muted already it stays muted
            }

            _wasMuted = null;
            PluginLog.Info("FarVolume: back at the screen, sound resumed");
        }

        private static void ReleaseLocked()
        {
            if (Stopped)
            {
                ResumeLocked(force: true);
            }

            if (Boosted && Restore && _original.HasValue)
            {
                Audio.SetVolume(_original.Value);
            }

            ForgetBoostLocked();
            _farSince = null;
            _nearSince = null;
        }

        public static String ToJson()
        {
            Boolean enabled, atDesk, boosted, stopped;
            Double? ratio, baseline;
            Int32? original;
            lock (Gate)
            {
                enabled = Enabled;
                atDesk = AtDesk;
                boosted = Boosted;
                stopped = Stopped;
                ratio = _ratio;
                baseline = Baseline;
                original = _original;
            }

            var volume = Audio.GetVolume();
            var muted = Audio.GetMuted();
            static String N(Double? d) => d.HasValue ? d.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null";
            static String I(Int32? i) => i.HasValue ? i.Value.ToString(CultureInfo.InvariantCulture) : "null";
            static String B(Boolean? b) => b.HasValue ? (b.Value ? "true" : "false") : "null";
            return $"{{\"enabled\":{B(enabled)},\"atDesk\":{B(atDesk)},\"ratio\":{N(ratio)},\"baseline\":{N(baseline)},\"boosted\":{B(boosted)},\"stopped\":{B(stopped)},\"original\":{I(original)},\"volume\":{I(volume)},\"muted\":{B(muted)},\"cap\":{CapPct},\"audio\":\"{Audio.Name}\"}}";
        }
    }
}
