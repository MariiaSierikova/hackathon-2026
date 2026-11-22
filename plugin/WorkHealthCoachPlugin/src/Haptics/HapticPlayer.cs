namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

        // The four patterns offered in the settings page (see eventMapping.yaml for the MX Master 4 waveforms).
        public const String Tap = "hapticTap";
        public const String Bump = "hapticBump";
        public const String AlertWave = "hapticAlert";
        public const String Jingle = "hapticJingle";

        public record Step(String Pulse, Int32 DelayAfterMs);

        // Pattern name = trigger name from docs/DESIGN.md.
        private static readonly Dictionary<String, Step[]> Patterns = new()
        {
            ["pace_too_fast"]    = new[] { new Step(Calm, 450), new Step(Calm, 450), new Step(Calm, 450), new Step(Calm, 0) },
            ["time_warning"]     = new[] { new Step(Sharp, 140), new Step(Sharp, 0) },
            ["blink_low"]        = new[] { new Step(Soft, 160), new Step(Soft, 0) },
            ["eye_contact_lost"] = new[] { new Step(Soft, 0) },
            ["filler_burst"]     = new[] { new Step(Knock, 0) },
            ["lookaway_done"]    = new[] { new Step(Done, 0) },
            ["speak_up"]         = new[] { new Step(Sharp, 220), new Step(Soft, 0) },   // "louder": a crisp tap then a soft one
            ["break_due"]        = new[] { new Step(Soft, 300), new Step(Soft, 300), new Step(Soft, 0) },
            ["test_blink"]       = new[] { new Step(Soft, 0) },   // development only: one soft tap per detected blink
            ["person_behind"]    = new[] { new Step(Knock, 350), new Step(Knock, 0) },
            ["speak_up"]         = new[] { new Step(Soft, 0) },
            ["speak_down"]       = new[] { new Step(Calm, 0) },
            ["break_due"]        = new[] { new Step(Done, 300), new Step(Done, 0) },
            ["break_over"]       = new[] { new Step(Sharp, 0) },
            ["volume_far"]       = new[] { new Step(Soft, 0) },
        };

        // User-chosen patterns (settings page): name -> pulses.
        private static readonly Dictionary<String, Step[]> Choices = new()
        {
            ["tap"]    = new[] { new Step(Tap, 0) },
            ["bump"]   = new[] { new Step(Bump, 0) },
            ["alert"]  = new[] { new Step(AlertWave, 0) },
            ["jingle"] = new[] { new Step(Jingle, 0) },
        };

        public static IEnumerable<String> ChoiceNames => Choices.Keys;

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
            plugin.PluginEvents.AddEvent(Tap, "Tap", "Short, crisp pulse");
            plugin.PluginEvents.AddEvent(Bump, "Bump", "Soft impact");
            plugin.PluginEvents.AddEvent(AlertWave, "Alert", "Attention-grabbing pattern");
            plugin.PluginEvents.AddEvent(Jingle, "Jingle", "Friendly, musical pattern");
        }

        // light = just the first tap; normal = the pattern; strong = the pattern twice.
        private static Step[] Scale(Step[] steps, String strength) => strength switch
        {
            "off" => Array.Empty<Step>(),
            "light" => new[] { new Step(steps[0].Pulse == Calm ? Calm : Soft, 0) },
            "strong" => steps.Take(steps.Length - 1)
                .Concat(new[] { new Step(steps[^1].Pulse, 450) })
                .Concat(steps).ToArray(),
            _ => steps,
        };

        // choice = one of tap | bump | alert | jingle chosen in the settings page (null = the trigger's built-in pattern); repeat = 1..3.
        public static Step[] Resolve(String patternName, String strength, String choice, Int32 repeat)
        {
            if (!String.IsNullOrEmpty(choice) && Choices.TryGetValue(choice, out var picked))
            {
                var once = strength switch
                {
                    "off" => Array.Empty<Step>(),
                    "strong" => picked.Take(picked.Length - 1).Concat(new[] { new Step(picked[^1].Pulse, 300) }).Concat(picked).ToArray(),
                    _ => picked,
                };
                var all = new List<Step>();
                for (var i = 0; i < Math.Clamp(repeat, 1, 3); i++)
                {
                    all.AddRange(once.Select((s, idx) => idx == once.Length - 1 && i < Math.Clamp(repeat, 1, 3) - 1 ? new Step(s.Pulse, 300) : s));
                }

                return all.ToArray();
            }

            return Patterns.TryGetValue(patternName, out var steps) ? Scale(steps, strength) : null;
        }

        public static void Play(String patternName, String strength = "normal", String choice = null, Int32 repeat = 1)
        {
            if (_plugin == null)
            {
                PluginLog.Warning($"HapticPlayer: not initialised, cannot play '{patternName}'");
                return;
            }

            var steps = Resolve(patternName, strength, choice, repeat);
            if (steps == null)
            {
                PluginLog.Warning($"HapticPlayer: unknown pattern '{patternName}'");
                return;
            }

            if (steps.Length == 0)
            {
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
