namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    // How one kind of trigger is allowed to reach the user.
    public class TriggerRule
    {
        public Boolean Enabled { get; set; } = true;
        public Boolean Haptic { get; set; } = true;       // mouse
        public Boolean Visual { get; set; } = true;       // keypad
        public Boolean Overlay { get; set; } = true;      // on-screen animal
        public Boolean Critical { get; set; }             // still delivered while snoozed
        public Double MinSeverity { get; set; } = 0.5;    // ignore weaker triggers
        public Int32 CooldownSec { get; set; } = 60;      // minimum time between alerts
        public Int32 HapticAfterSec { get; set; }         // escalation: visual first, mouse only if it persists this long
        public Int32 MaxPer10Min { get; set; } = 4;       // alert budget

        // ---- chosen in the settings page (all optional; null/false = the trigger's built-in behaviour) ----
        public String HapticPattern { get; set; }          // tap | bump | alert | jingle (null = built-in pattern)
        public Int32 HapticRepeat { get; set; } = 1;       // 1..3
        public String KeypadColor { get; set; }            // "#rrggbb" for the full-Keypad reminder (null = default)
        public Boolean Audio { get; set; }                 // sound channel (played by the app/overlay side, not the plugin)
        public String Sound { get; set; }
        public String ScreenStyle { get; set; }            // popup | banner | edge
        public String ScreenText { get; set; }
        public String ScreenText2 { get; set; }
    }

    public class PolicySettings
    {
        public String Preset { get; set; } = "balanced";           // quiet | balanced | attentive | custom
        public String HapticStrength { get; set; } = "normal";     // off | light | normal | strong
        public Int32 TotalMinutes { get; set; } = 20;
        public Int32 WarnMinutes { get; set; } = 1;
        public Boolean Master { get; set; } = true;                // master switch: off = nothing reaches the user
        public String Profile { get; set; } = "everyday";          // settings-page profile (everyday | presenting | focus | meeting | custom)
        public List<String> Muted { get; set; } = new();           // channels silenced by the profile: audio | screen | haptic | keypad
        public Dictionary<String, TriggerRule> Triggers { get; set; } = new();

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static readonly String[] Presets = { "quiet", "balanced", "attentive" };
        public static readonly String[] Strengths = { "off", "light", "normal", "strong" };

        // WORKHEALTH_DATA_DIR overrides the folder (tests/harness); normally ~/Library/Application Support/WorkHealthCoach or %APPDATA%\WorkHealthCoach.
        public static String DataDir => Environment.GetEnvironmentVariable("WORKHEALTH_DATA_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkHealthCoach");

        public static String FilePath => Path.Combine(DataDir, "settings.json");

        // Balanced baseline for every trigger.
        private static Dictionary<String, TriggerRule> Baseline() => new()
        {
            ["pace_too_fast"]    = new TriggerRule { MinSeverity = 0.5, CooldownSec = 90,  HapticAfterSec = 5,  MaxPer10Min = 4 },
            ["time_warning"]     = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 3, Critical = true },
            ["filler_burst"]     = new TriggerRule { MinSeverity = 0.6, CooldownSec = 120, HapticAfterSec = 0,  MaxPer10Min = 3 },
            ["eye_contact_lost"] = new TriggerRule { MinSeverity = 0.6, CooldownSec = 120, HapticAfterSec = 10, MaxPer10Min = 3 },
            ["blink_low"]        = new TriggerRule { MinSeverity = 0.5, CooldownSec = 300, HapticAfterSec = 0,  MaxPer10Min = 2 },
            ["lookaway_done"]    = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 6 },
            ["speak_up"]         = new TriggerRule { MinSeverity = 0.5, CooldownSec = 90,  HapticAfterSec = 5,  MaxPer10Min = 4 },
            ["break_due"]        = new TriggerRule { MinSeverity = 0.5, CooldownSec = 180, HapticAfterSec = 5,  MaxPer10Min = 3 },
            ["person_behind"]    = new TriggerRule { MinSeverity = 0.5, CooldownSec = 20,  HapticAfterSec = 0,  MaxPer10Min = 6, Critical = true },
            ["test_blink"]       = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 999 },
            // added with the settings page (team table 2026-11-21)
            ["speak_up"]         = new TriggerRule { MinSeverity = 0.5, CooldownSec = 30,  HapticAfterSec = 0,  MaxPer10Min = 6 },
            ["speak_down"]       = new TriggerRule { MinSeverity = 0.5, CooldownSec = 30,  HapticAfterSec = 0,  MaxPer10Min = 6 },
            ["break_due"]        = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 30 },
            ["break_over"]       = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 30 },
            ["volume_far"]       = new TriggerRule { MinSeverity = 0.0, CooldownSec = 0,   HapticAfterSec = 0,  MaxPer10Min = 999, Haptic = false, Visual = false, Overlay = false },
        };

        // Quiet = only what really matters; Attentive = more frequent coaching.
        public void ApplyPreset(String name)
        {
            name = (name ?? "balanced").ToLowerInvariant();
            var rules = Baseline();
            foreach (var (trigger, r) in rules)
            {
                var important = r.Critical || trigger == "test_blink" || trigger == "lookaway_done";
                if (name == "quiet" && !important)
                {
                    r.MinSeverity = Math.Max(r.MinSeverity, 0.8);
                    r.CooldownSec *= 3;
                    r.HapticAfterSec = r.HapticAfterSec * 3 + 10;
                    r.MaxPer10Min = Math.Max(1, r.MaxPer10Min / 2);
                    r.Haptic = trigger == "pace_too_fast";   // mouse only for the clearest problem; the rest stay visual
                }
                else if (name == "attentive" && !important)
                {
                    r.MinSeverity = Math.Min(r.MinSeverity, 0.3);
                    r.CooldownSec = Math.Max(15, r.CooldownSec / 2);
                    r.HapticAfterSec = 0;
                    r.MaxPer10Min *= 2;
                }
            }

            this.Triggers = rules;
            this.Preset = Array.IndexOf(Presets, name) >= 0 ? name : "balanced";
        }

        public static PolicySettings Defaults()
        {
            var s = new PolicySettings();
            s.ApplyPreset("balanced");
            return s;
        }

        public static PolicySettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<PolicySettings>(File.ReadAllText(FilePath), Json);
                    if (s != null)
                    {
                        // keep newly added triggers working after an upgrade
                        foreach (var (k, v) in Baseline())
                        {
                            s.Triggers.TryAdd(k, v);
                        }

                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not read settings, using defaults");
            }

            return Defaults();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, this.ToJson());
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not save settings");
            }
        }

        public String ToJson() => JsonSerializer.Serialize(this, Json);

        public static PolicySettings FromJson(String json) => JsonSerializer.Deserialize<PolicySettings>(json, Json);
    }
}
