namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // What actually reaches the user for one trigger.
    public record Decision(TriggerMessage Message, Boolean Haptic, Boolean Visual, Boolean Overlay, String HapticStrength);

    // Sits between raw triggers (camera/mic) and the outputs (mouse, Keypad, overlay) so alerts never become annoying.
    // Rules, in order: snooze -> enabled -> min severity -> episode/escalation -> cooldown (stretched after dismissals) -> budget.
    public static class NotificationPolicy
    {
        private class State
        {
            public DateTime EpisodeStart = DateTime.MinValue;
            public DateTime LastSeen = DateTime.MinValue;
            public DateTime LastFired = DateTime.MinValue;
            public Boolean VisualShownInEpisode;
            public readonly List<DateTime> Fired = new();
        }

        private static readonly Object Gate = new();
        private static readonly Dictionary<String, State> States = new();
        private static readonly List<DateTime> Dismissals = new();

        public static PolicySettings Settings { get; private set; } = PolicySettings.Defaults();
        public static DateTime SnoozeUntil { get; private set; } = DateTime.MinValue;
        public static Boolean Snoozed => DateTime.UtcNow < SnoozeUntil;

        public static event Action<Decision> Decided;
        public static event Action Changed;   // settings / snooze changed (for key labels)

        public static void Init()
        {
            Settings = PolicySettings.Load();
            Settings.Save();
            ApplySessionSettings();
        }

        private static void ApplySessionSettings()
        {
            SessionState.TotalMinutes = Settings.TotalMinutes;
            SessionState.WarnMinutes = Settings.WarnMinutes;
        }

        // ---- user controls ----
        public static void SetSettings(PolicySettings s)
        {
            Settings = s;
            Settings.Save();
            ApplySessionSettings();
            Changed?.Invoke();
        }

        public static void CyclePreset()
        {
            var i = Array.IndexOf(PolicySettings.Presets, Settings.Preset);
            Settings.ApplyPreset(PolicySettings.Presets[(i + 1) % PolicySettings.Presets.Length]);
            Settings.Save();
            Changed?.Invoke();
        }

        public static void SetPreset(String name)
        {
            Settings.ApplyPreset(name);
            Settings.Save();
            Changed?.Invoke();
        }

        public static void CycleStrength()
        {
            var i = Array.IndexOf(PolicySettings.Strengths, Settings.HapticStrength);
            Settings.HapticStrength = PolicySettings.Strengths[(i + 1) % PolicySettings.Strengths.Length];
            Settings.Save();
            Changed?.Invoke();
        }

        public static void Snooze(Int32 minutes)
        {
            SnoozeUntil = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : DateTime.MinValue;
            if (minutes > 0)
            {
                Dismiss();
            }

            Changed?.Invoke();
        }

        public static void ToggleSnooze() => Snooze(Snoozed ? 0 : 15);

        // The user waved an alert away. Two dismissals in 10 minutes make every cooldown longer.
        public static void Dismiss()
        {
            lock (Gate)
            {
                Dismissals.Add(DateTime.UtcNow);
            }

            Changed?.Invoke();
        }

        private static Double Backoff()
        {
            lock (Gate)
            {
                Dismissals.RemoveAll(t => (DateTime.UtcNow - t).TotalMinutes > 10);
                return Dismissals.Count >= 2 ? Math.Min(4, Dismissals.Count - 1) : 1;
            }
        }

        // ---- the decision ----
        public static void Evaluate(TriggerMessage m)
        {
            var d = Decide(m, DateTime.UtcNow);
            if (d != null)
            {
                Decided?.Invoke(d);
            }
        }

        public static Decision Decide(TriggerMessage m, DateTime now)
        {
            if (m.Force)
            {
                // Development switch: show/buzz everything, ignore cooldowns, budget and snooze, leave the policy state untouched.
                PluginLog.Info($"Policy: {m.Trigger} FORCED (policy skipped)");
                return new Decision(m, Settings.HapticStrength != "off", true, true, Settings.HapticStrength);
            }

            if (!Settings.Triggers.TryGetValue(m.Trigger, out var rule))
            {
                return Drop(m, "no rule");
            }

            if (!rule.Enabled)
            {
                return Drop(m, "disabled");
            }

            if (Snoozed && !rule.Critical)
            {
                return Drop(m, "snoozed");
            }

            if (m.Severity < rule.MinSeverity)
            {
                return Drop(m, $"severity {m.Severity:0.0} < {rule.MinSeverity:0.0}");
            }

            lock (Gate)
            {
                if (!States.TryGetValue(m.Trigger, out var st))
                {
                    States[m.Trigger] = st = new State();
                }

                // An "episode" = the problem keeps being reported; it ends after 30 s of silence.
                if ((now - st.LastSeen).TotalSeconds > 30)
                {
                    st.EpisodeStart = now;
                    st.VisualShownInEpisode = false;
                }

                st.LastSeen = now;

                var cooldown = rule.CooldownSec * Backoff();
                if ((now - st.LastFired).TotalSeconds < cooldown)
                {
                    return Drop(m, $"cooldown ({cooldown:0}s)");
                }

                st.Fired.RemoveAll(t => (now - t).TotalMinutes > 10);
                if (st.Fired.Count >= rule.MaxPer10Min)
                {
                    return Drop(m, $"budget {rule.MaxPer10Min}/10min used");
                }

                var strengthOff = Settings.HapticStrength == "off";
                var wantHaptic = rule.Haptic && !strengthOff;
                var escalated = (now - st.EpisodeStart).TotalSeconds >= rule.HapticAfterSec;

                if (wantHaptic && !escalated)
                {
                    // Escalation step 1: only show it on the Keypad, once per episode; the mouse waits.
                    if (st.VisualShownInEpisode)
                    {
                        return Drop(m, $"waiting {rule.HapticAfterSec}s before the mouse buzzes");
                    }

                    st.VisualShownInEpisode = true;
                    PluginLog.Info($"Policy: {m.Trigger} -> visual only (mouse after {rule.HapticAfterSec}s if it persists)");
                    return new Decision(m, false, rule.Visual, rule.Overlay, Settings.HapticStrength);
                }

                // Delivered: starts cooldown and uses one budget slot.
                st.LastFired = now;
                st.Fired.Add(now);
                st.VisualShownInEpisode = true;
                PluginLog.Info($"Policy: {m.Trigger} -> {(wantHaptic ? "haptic " : string.Empty)}{(rule.Visual ? "visual " : string.Empty)}{(rule.Overlay ? "overlay" : string.Empty)} [{Settings.HapticStrength}, {Settings.Preset}]");
                return new Decision(m, wantHaptic, rule.Visual, rule.Overlay, Settings.HapticStrength);
            }
        }

        private static Decision Drop(TriggerMessage m, String why)
        {
            PluginLog.Info($"Policy: {m.Trigger} dropped: {why}");
            return null;
        }
    }
}
