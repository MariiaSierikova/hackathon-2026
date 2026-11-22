namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;

    // State of the settings page (web/index.html), stored as one JSON document: ui-state.json next to settings.json.
    // The page owns the full document (feedback texts, images, sounds ...). On every change the plugin also maps the
    // parts it can act on onto the notification policy (enabled, cooldown, channels, haptic pattern, Keypad colour) and the
    // Pomodoro timer, so real triggers follow what the user set on the page.
    // The MX Keypad keys (FeatureToggleCommand) change the same document, so page, keys and policy never disagree.
    public static class UiSettings
    {
        private static readonly Object Gate = new();
        private static JsonObject _state = new();

        public static Int64 Rev { get; private set; }

        public static event Action Changed;

        public static String FilePath => Path.Combine(Path.GetDirectoryName(PolicySettings.FilePath), "ui-state.json");

        // settings-page feature -> the plugin triggers it controls
        public static readonly Dictionary<String, String[]> TriggersByFeature = new()
        {
            ["speed"] = new[] { "pace_too_fast" },
            ["volume"] = new[] { "speak_up", "speak_down" },
            ["blink"] = new[] { "blink_low" },
            ["pomodoro"] = new[] { "break_due", "break_over" },
            ["distance"] = new[] { "volume_far" },
            ["behind"] = new[] { "person_behind" },
        };

        // Keep in sync with PROFILES in web/index.html.
        public static readonly Dictionary<String, (String[] On, String[] Mute)> Profiles = new()
        {
            ["everyday"] = (new[] { "speed", "volume", "blink", "distance", "behind" }, Array.Empty<String>()),
            ["presenting"] = (new[] { "speed", "volume", "behind" }, new[] { "audio", "screen" }),
            ["focus"] = (new[] { "blink", "pomodoro" }, new[] { "audio" }),
            ["meeting"] = (new[] { "speed", "volume", "blink", "behind" }, new[] { "audio" }),
        };

        public static void Init()
        {
            try
            {
                if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
                {
                    lock (Gate)
                    {
                        _state = o;
                        Rev = o["rev"]?.GetValue<Int64>() ?? 1;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not read ui-state.json, starting empty");
            }

            Apply();
        }

        public static Boolean HasState
        {
            get { lock (Gate) { return _state["features"] is JsonObject; } }
        }

        public static String ToJson()
        {
            lock (Gate)
            {
                _state["rev"] = Rev;
                return _state.ToJsonString();
            }
        }

        // Replaces the whole document (the settings page sends it).
        public static Boolean Set(String json, out String error)
        {
            error = null;
            JsonObject o;
            try
            {
                o = JsonNode.Parse(json) as JsonObject;
            }
            catch (Exception ex)
            {
                error = "invalid json: " + ex.Message;
                return false;
            }

            if (o == null || o["features"] is not JsonObject)
            {
                error = "missing \"features\"";
                return false;
            }

            lock (Gate)
            {
                _state = o;
                Rev++;
            }

            Commit();
            return true;
        }

        // ---- changes from the MX Keypad keys ----
        public static Boolean Master => Bool(Get("master"), true);

        public static String Profile => Str(Get("profile"), "everyday");

        public static Boolean FeatureEnabled(String id)
        {
            var node = Get("features", id, "enabled");
            if (node != null)
            {
                return Bool(node, false);
            }

            return TriggersByFeature.TryGetValue(id, out var t) && NotificationPolicy.Settings.Triggers.TryGetValue(t[0], out var rule) && rule.Enabled;
        }

        public static void SetFeatureEnabled(String id, Boolean on)
        {
            lock (Gate)
            {
                var f = Ensure(Ensure(_state, "features"), id);
                f["enabled"] = on;
                _state["profile"] = "custom";
                _state["muted"] = new JsonArray();
                Rev++;
            }

            Commit();
        }

        public static void SetMaster(Boolean on)
        {
            lock (Gate)
            {
                _state["master"] = on;
                Rev++;
            }

            Commit();
        }

        public static void ApplyProfile(String name)
        {
            if (!Profiles.TryGetValue(name, out var p))
            {
                return;
            }

            lock (Gate)
            {
                var features = Ensure(_state, "features");
                foreach (var id in TriggersByFeature.Keys)
                {
                    Ensure(features, id)["enabled"] = p.On.Contains(id);
                }

                _state["profile"] = name;
                _state["master"] = true;
                _state["muted"] = new JsonArray(p.Mute.Select(m => (JsonNode)JsonValue.Create(m)).ToArray());
                Rev++;
            }

            Commit();
        }

        public static void TogglePresenting() => ApplyProfile(Profile == "presenting" ? "everyday" : "presenting");

        // ---- plumbing ----
        private static void Commit()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                String text;
                lock (Gate)
                {
                    _state["rev"] = Rev;
                    text = _state.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                }

                File.WriteAllText(FilePath, text);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not save ui-state.json");
            }

            Apply();
            Changed?.Invoke();
        }

        private static JsonNode Get(params String[] path)
        {
            lock (Gate)
            {
                JsonNode n = _state;
                foreach (var p in path)
                {
                    n = (n as JsonObject)?[p];
                    if (n == null)
                    {
                        return null;
                    }
                }

                return n;
            }
        }

        private static JsonObject Ensure(JsonObject parent, String key)
        {
            if (parent[key] is not JsonObject child)
            {
                child = new JsonObject();
                parent[key] = child;
            }

            return child;
        }

        private static Boolean Bool(JsonNode n, Boolean def) =>
            n is JsonValue v && v.TryGetValue<Boolean>(out var b) ? b : def;

        private static Int32 Int(JsonNode n, Int32 def)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue<Int32>(out var i))
                {
                    return i;
                }

                if (v.TryGetValue<Double>(out var d))
                {
                    return (Int32)Math.Round(d);
                }
            }

            return def;
        }

        private static String Str(JsonNode n, String def) =>
            n is JsonValue v && v.TryGetValue<String>(out var s) && !String.IsNullOrEmpty(s) ? s : def;

        // Document -> policy rules + Pomodoro timer.
        public static void Apply()
        {
            JsonObject st;
            lock (Gate)
            {
                st = (JsonObject)_state.DeepClone();
            }

            var s = NotificationPolicy.Settings;
            s.Master = Bool(st["master"], true);
            s.Profile = Str(st["profile"], s.Profile);
            s.Muted = st["muted"] is JsonArray arr
                ? arr.Select(x => x is JsonValue v && v.TryGetValue<String>(out var m) ? m : null).Where(x => x != null).ToList()
                : (Profiles.TryGetValue(s.Profile, out var pr) ? pr.Mute.ToList() : new List<String>());

            foreach (var (fid, triggers) in TriggersByFeature)
            {
                if (st["features"]?[fid] is not JsonObject f)
                {
                    continue;
                }

                foreach (var t in triggers)
                {
                    if (!s.Triggers.TryGetValue(t, out var rule))
                    {
                        continue;
                    }

                    rule.Enabled = Bool(f["enabled"], rule.Enabled);

                    // The plugin's Pomodoro timer sends break_due / break_over ONCE per phase, so the mouse must buzz on the first
                    // message (an existing settings.json may still carry the old "mouse after 5 s" escalation for break_due).
                    if (t is "break_due" or "break_over")
                    {
                        rule.HapticAfterSec = 0;
                    }
                    if (f["cooldown"] != null)
                    {
                        rule.CooldownSec = Math.Max(0, Int(f["cooldown"], rule.CooldownSec));
                    }

                    if (f["ch"] is JsonObject ch)
                    {
                        rule.Haptic = Bool(ch["haptic"], rule.Haptic);
                        rule.Visual = Bool(ch["keypad"], rule.Visual);
                        rule.Overlay = Bool(ch["screen"], rule.Overlay);
                        rule.Audio = Bool(ch["audio"], rule.Audio);
                    }

                    var fb = f["fb"];
                    rule.HapticPattern = Str(fb?["haptic"]?["pattern"], null);
                    rule.HapticRepeat = Math.Clamp(Int(fb?["haptic"]?["repeat"], 1), 1, 3);
                    rule.KeypadColor = Str(fb?["keypad"]?["mode"], "color") == "color" ? Str(fb?["keypad"]?["color"], null) : null;
                    rule.Sound = Str(fb?["audio"]?["sound"], null);
                    rule.ScreenStyle = Str(fb?["screen"]?["style"], null);
                    rule.ScreenText = Str(fb?["screen"]?["texts"]?["text"], null);
                    rule.ScreenText2 = Str(fb?["screen"]?["texts"]?["text2"], null);
                }
            }

            s.Preset = "custom";
            NotificationPolicy.SetSettings(s);

            if (st["features"]?["pomodoro"] is JsonObject p)
            {
                var pr2 = p["params"];
                PomodoroState.Configure(
                    Int(pr2?["work"], 25), Int(pr2?["short"], 5), Int(pr2?["long"], 15), Int(pr2?["rounds"], 4),
                    Bool(pr2?["auto"], false), Bool(pr2?["cam"], true), Int(pr2?["reset"], 5));

                // feature on <=> the timer is running on the Keypad
                var on = Bool(p["enabled"], false);
                if (on != PomodoroState.Active)
                {
                    if (on)
                    {
                        PomodoroState.Start();
                    }
                    else
                    {
                        PomodoroState.Stop();
                    }
                }
            }
        }
    }
}
