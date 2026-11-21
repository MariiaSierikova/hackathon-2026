namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // Plays named haptic PATTERNS (sequences of pulses) on the MX Master 4.
    // The haptics API has no duration setting, so a "long calm" buzz is several pulses in a row.
    // A new pattern cancels the one that is still playing, so alerts never pile up.
    public static class HapticPlayer
    {
        public const String Soft = "pulseSoft";
        public const String Calm = "pulseCalm";
        public const String Sharp = "pulseSharp";
        public const String Knock = "pulseKnock";
        public const String Done = "pulseDone";

        private record Step(String Pulse, Int32 DelayAfterMs);

        // Pattern name = trigger name from docs/DESIGN.md.
        private static readonly Dictionary<String, Step[]> Patterns = new()
        {
            ["pace_too_fast"]    = new[] { new Step(Calm, 450), new Step(Calm, 450), new Step(Calm, 450), new Step(Calm, 0) },
            ["time_warning"]     = new[] { new Step(Sharp, 140), new Step(Sharp, 0) },
            ["blink_low"]        = new[] { new Step(Soft, 160), new Step(Soft, 0) },
            ["eye_contact_lost"] = new[] { new Step(Soft, 0) },
            ["filler_burst"]     = new[] { new Step(Knock, 0) },
            ["lookaway_done"]    = new[] { new Step(Done, 0) },
            ["test_blink"]       = new[] { new Step(Soft, 0) },   // development only: one soft tap per detected blink
            ["person_behind"]    = new[] { new Step(Knock, 350), new Step(Knock, 0) },
        };

        private static Plugin _plugin;
        private static CancellationTokenSource _cts;
        private static readonly Object Gate = new();

        public static IEnumerable<String> PatternNames => Patterns.Keys;

        // Call once from Plugin.Load().
        public static void Init(Plugin plugin)
        {
            _plugin = plugin;
            plugin.PluginEvents.AddEvent(Soft, "Soft pulse", "Light tap");
            plugin.PluginEvents.AddEvent(Calm, "Calm pulse", "Slow wave");
            plugin.PluginEvents.AddEvent(Sharp, "Sharp pulse", "Crisp tap");
            plugin.PluginEvents.AddEvent(Knock, "Knock", "Knocking pattern");
            plugin.PluginEvents.AddEvent(Done, "Done pulse", "Task completed");
        }

        public static void Play(String patternName)
        {
            if (_plugin == null || !Patterns.TryGetValue(patternName, out var steps))
            {
                PluginLog.Warning($"HapticPlayer: unknown pattern '{patternName}' or not initialised");
                return;
            }

            CancellationTokenSource cts;
            lock (Gate)
            {
                _cts?.Cancel();
                _cts = cts = new CancellationTokenSource();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var step in steps)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        _plugin.PluginEvents.RaiseEvent(step.Pulse);
                        if (step.DelayAfterMs > 0)
                        {
                            await Task.Delay(step.DelayAfterMs, cts.Token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { PluginLog.Error(ex, $"HapticPlayer: pattern '{patternName}' failed"); }
            });
        }
    }
}
