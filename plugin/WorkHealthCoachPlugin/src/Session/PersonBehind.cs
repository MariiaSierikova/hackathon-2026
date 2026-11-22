namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;

    // "Person behind you": alerts only when someone COMES TO the user, not when they just walk past.
    //
    // Input (no camera access here): samples of a second face seen behind the user, from any detector or the page's simulator
    //   POST /behind {"id":2,"x":0.62,"size":0.07}      x = 0 (left) .. 1 (right) in the picture, size = face width / picture width
    //   POST /behind/clear                                nobody visible
    // The engine follows each person (by id) for a few seconds and decides:
    //   walking past   sideways speed above PassSpeed (in face widths per second: independent of distance), little growth
    //   coming         the face grows by ApproachPct or more within about 3 s without crossing sideways
    //   stays          seen for LingerSec or more and hardly moving sideways (someone standing behind the chair)
    //   too far        face smaller than a person at RangeM metres (about 0.08 of the picture width at 1 m)
    // With "ignore people who walk past" off, any face seen for HoldSec seconds alerts (the team table's plain rule).
    // One alert per visit. An alert publishes the trigger `person_behind` (policy: cooldown, haptics, Keypad, screen)
    // and runs the automatic response from BehindResponse (nothing, hide windows, lock the screen).
    public static class PersonBehind
    {
        private sealed class Track
        {
            public Int32 Id;
            public readonly List<(DateTime At, Double X, Double Size)> Pts = new();
            public DateTime FirstSeen;
            public DateTime LastSeen;
            public Boolean Alerted;
            public String Verdict = "waiting";
        }

        private static readonly Object Gate = new();
        private static readonly Dictionary<Int32, Track> Tracks = new();
        private static readonly List<String> History = new();

        // ---- settings (from the settings page, see UiSettings) ----
        public static Boolean Enabled { get; private set; } = true;

        public static Double HoldSec { get; private set; } = 2;

        public static Double RangeM { get; private set; } = 3;

        public static Boolean FilterPassBy { get; private set; } = true;

        public static Double LingerSec { get; private set; } = 3;

        public static Int32 ApproachPct { get; private set; } = 25;

        public static Double PassSpeed { get; private set; } = 2.5;     // face widths per second sideways: faster = walking past

        public static Double LingerSpeed { get; private set; } = 1.0;   // slower than this = standing

        public static Int32 AckSec { get; private set; } = 60;

        public const Double SizeAtOneMeter = 0.08;   // face width / picture width at 1 m (90 degree lens, 16 cm face)

        public static Double MinSize => SizeAtOneMeter / Math.Max(0.5, RangeM);

        public static Func<DateTime> Now = () => DateTime.UtcNow;

        // ---- state ----
        public static DateTime? AckUntil { get; private set; }

        public static String LastReason { get; private set; }

        public static DateTime? LastAlertAt { get; private set; }

        private static DateTime _lastAnySeen = DateTime.MinValue;

        public static void Configure(Boolean enabled, Double holdSec, Double rangeM, Boolean filterPassBy, Double lingerSec)
        {
            lock (Gate)
            {
                Enabled = enabled;
                HoldSec = Math.Clamp(holdSec, 0.5, 30);
                RangeM = Math.Clamp(rangeM, 0.5, 10);
                FilterPassBy = filterPassBy;
                LingerSec = Math.Clamp(lingerSec, 1, 60);
                if (!enabled)
                {
                    Tracks.Clear();
                }
            }
        }

        // ---- input ----
        public static void AddSample(Int32 id, Double x, Double size)
        {
            lock (Gate)
            {
                if (!Enabled)
                {
                    return;
                }

                var now = Now();
                if (!Tracks.TryGetValue(id, out var t) || now - t.LastSeen > TimeSpan.FromSeconds(1.5))
                {
                    if (t != null)
                    {
                        Archive(t);
                    }

                    t = new Track { Id = id, FirstSeen = now };
                    Tracks[id] = t;
                }

                t.Pts.Add((now, x, size));
                t.LastSeen = now;
                _lastAnySeen = now;
                t.Pts.RemoveAll(p => now - p.At > TimeSpan.FromSeconds(8));
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                foreach (var t in Tracks.Values)
                {
                    Archive(t);
                }

                Tracks.Clear();
            }
        }

        // The user said "it's fine": ignore whoever is there now, and new visitors for AckSec seconds.
        public static void Acknowledge()
        {
            lock (Gate)
            {
                AckUntil = Now().AddSeconds(AckSec);
                foreach (var t in Tracks.Values)
                {
                    t.Alerted = true;
                }
            }

            SessionState.DismissAlert();
            NotificationPolicy.Dismiss();
        }

        // Called once a second by the plugin.
        public static void Tick()
        {
            String reason = null;
            Double severity = 0;
            Boolean someoneHere;
            lock (Gate)
            {
                var now = Now();
                foreach (var gone in Tracks.Values.Where(t => now - t.LastSeen > TimeSpan.FromSeconds(3)).ToList())
                {
                    Archive(gone);
                    Tracks.Remove(gone.Id);
                }

                if (Enabled && !(AckUntil.HasValue && now < AckUntil.Value))
                {
                    foreach (var t in Tracks.Values.Where(t => !t.Alerted && now - t.LastSeen <= TimeSpan.FromSeconds(1.5)))
                    {
                        var (alert, why, sev) = Evaluate(t, now);
                        if (alert && reason == null)
                        {
                            t.Alerted = true;
                            reason = why;
                            severity = sev;
                            t.Verdict = "ALERT: " + why;
                        }
                    }
                }

                someoneHere = Tracks.Values.Any(t => now - t.LastSeen <= TimeSpan.FromSeconds(1.5));
                if (reason != null)
                {
                    LastReason = reason;
                    LastAlertAt = now;
                    PluginLog.Info($"PersonBehind: someone is coming ({reason}), severity {severity:0.0}");
                }
            }

            if (reason != null)
            {
                TriggerBus.Publish(new TriggerMessage("person_behind", severity));
                BehindResponse.OnAlert();
            }

            BehindResponse.Tick(someoneHere, Now() - _lastAnySeen);
        }

        private static (Boolean alert, String reason, Double severity) Evaluate(Track t, DateTime now)
        {
            var seen = (t.LastSeen - t.FirstSeen).TotalSeconds;
            if (seen < HoldSec)
            {
                t.Verdict = "waiting";
                return (false, null, 0);
            }

            var recent = t.Pts.Where(p => now - p.At <= TimeSpan.FromSeconds(3)).ToList();
            if (recent.Count < 3)
            {
                t.Verdict = "waiting";
                return (false, null, 0);
            }

            var meanSize = recent.TakeLast(5).Average(p => p.Size);
            if (meanSize < MinSize)
            {
                t.Verdict = $"too far away (face {meanSize:0.000} < {MinSize:0.000})";
                return (false, null, 0);
            }

            if (!FilterPassBy)
            {
                return (true, $"a second face for {seen:0.0} s", 0.6);
            }

            var n = Math.Max(1, Math.Min(3, recent.Count / 2));
            var first = recent.Take(n).ToList();
            var last = recent.TakeLast(n).ToList();
            var dt = (last.Average(p => p.At.Ticks) - first.Average(p => p.At.Ticks)) / TimeSpan.TicksPerSecond;
            if (dt < 0.5)
            {
                t.Verdict = "waiting";
                return (false, null, 0);
            }

            var sideways = Math.Abs(last.Average(p => p.X) - first.Average(p => p.X)) / Math.Max(meanSize, 0.001) / dt;
            var growth = last.Average(p => p.Size) / Math.Max(first.Average(p => p.Size), 0.001);

            if (growth >= 1 + ApproachPct / 100.0 && sideways <= PassSpeed)
            {
                return (true, $"coming closer (+{(growth - 1) * 100:0} %)", 0.9);
            }

            if (seen >= LingerSec && sideways <= LingerSpeed)
            {
                return (true, $"stays behind you ({seen:0} s)", 0.7);
            }

            t.Verdict = sideways > PassSpeed
                ? $"walking past ({sideways:0.0} face widths/s sideways)"
                : $"watching (sideways {sideways:0.0}/s, growth {(growth - 1) * 100:0} %)";
            return (false, null, 0);
        }

        private static void Archive(Track t)
        {
            var seen = (t.LastSeen - t.FirstSeen).TotalSeconds;
            var text = t.Alerted
                ? $"person {t.Id}: alerted ({seen:0.0} s)"
                : $"person {t.Id}: passed by, no alert ({seen:0.0} s, {t.Verdict})";
            History.Add(text);
            if (History.Count > 6)
            {
                History.RemoveAt(0);
            }
        }

        public static String ToJson()
        {
            lock (Gate)
            {
                var now = Now();
                var tracks = Tracks.Values.Select(t =>
                {
                    var p = t.Pts.LastOrDefault();
                    return new
                    {
                        id = t.Id,
                        seen = Math.Round((t.LastSeen - t.FirstSeen).TotalSeconds, 1),
                        x = Math.Round(p.X, 3),
                        size = Math.Round(p.Size, 3),
                        verdict = t.Verdict,
                        alerted = t.Alerted,
                    };
                }).ToList();
                return JsonSerializer.Serialize(new
                {
                    enabled = Enabled,
                    tracks,
                    lastAlert = LastReason,
                    ack = AckUntil.HasValue && now < AckUntil.Value,
                    hidden = BehindResponse.Hidden,
                    talk = BehindResponse.Talk,
                    musicPaused = BehindResponse.MusicPaused,
                    history = History.ToList(),
                    minSize = Math.Round(MinSize, 3),
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
        }
    }
}
