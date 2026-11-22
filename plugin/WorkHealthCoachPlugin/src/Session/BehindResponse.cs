namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Globalization;

    // What the user can DO when someone comes to the desk. Used automatically (setting "When someone comes to me")
    // and by hand from the Actions Ring / Keypad (PersonBehindCommand) and the settings page.
    //   hide    press the user's own "hide my windows" shortcut           (recorded on the page, e.g. Cmd+Opt+H or Win+D)
    //   back    press the optional "bring them back" shortcut
    //   lock    lock the screen (macOS Ctrl+Cmd+Q, Windows Win+L)
    //   music   Play/Pause media key (a toggle: it would START music if nothing was playing)
    //   talk    "talk mode": lower the system volume so the user can hear the colleague, press again to restore
    //   ok      "it's fine": dismiss and ignore visitors for a minute
    //   snooze  alerts off for 15 minutes
    public static class BehindResponse
    {
        public static String Act { get; private set; } = "notify";   // notify | hide | lock

        public static String HideShortcut { get; private set; } = String.Empty;

        public static String BackShortcut { get; private set; } = String.Empty;

        public static Int32 BackSec { get; private set; }            // 0 = never bring windows back automatically

        public static Int32 DuckPct { get; private set; } = 30;

        // wiring: set by the plugin, replaced by tests
        public static Action<VirtualKeyCode, ModifierKey> SendShortcut;
        public static Action SendPlayPause;

        public static Boolean Hidden { get; private set; }

        public static Boolean MusicPaused { get; private set; }

        public static Boolean Talk { get; private set; }

        private static Int32? _talkOrig;
        private static Int32? _talkSet;

        public static void Configure(String act, String hideShortcut, String backShortcut, Int32 backSec, Int32 duckPct)
        {
            Act = act ?? "notify";
            HideShortcut = hideShortcut ?? String.Empty;
            BackShortcut = backShortcut ?? String.Empty;
            BackSec = Math.Max(0, backSec);
            DuckPct = Math.Clamp(duckPct, 0, 100);
        }

        // Automatic response to a confirmed visitor.
        public static void OnAlert()
        {
            switch (Act)
            {
                case "hide":
                    Hide();
                    break;
                case "lock":
                    Lock();
                    break;
            }
        }

        // The visitor has gone: bring the windows back if the user asked for it.
        public static void Tick(Boolean someoneHere, TimeSpan sinceLastSeen)
        {
            if (Hidden && BackSec > 0 && !someoneHere && sinceLastSeen >= TimeSpan.FromSeconds(BackSec))
            {
                Back();
            }
        }

        public static Boolean Hide()
        {
            if (!TryParseShortcut(HideShortcut, out var key, out var mods))
            {
                PluginLog.Warning($"BehindResponse: no valid hide shortcut ('{HideShortcut}'), record one on the settings page");
                return false;
            }

            SendShortcut?.Invoke(key, mods);
            Hidden = true;
            PluginLog.Info($"BehindResponse: pressed the hide shortcut '{HideShortcut}'");
            return true;
        }

        public static Boolean Back()
        {
            Hidden = false;
            if (!TryParseShortcut(BackShortcut, out var key, out var mods))
            {
                PluginLog.Info("BehindResponse: no 'bring back' shortcut set, nothing to press");
                return false;
            }

            SendShortcut?.Invoke(key, mods);
            PluginLog.Info($"BehindResponse: pressed the bring-back shortcut '{BackShortcut}'");
            return true;
        }

        public static void Lock()
        {
            if (OperatingSystem.IsMacOS())
            {
                SendShortcut?.Invoke(VirtualKeyCode.KeyQ, ModifierKey.Control | ModifierKey.Command);
            }
            else if (OperatingSystem.IsWindows())
            {
                SendShortcut?.Invoke(VirtualKeyCode.KeyL, ModifierKey.Windows);
            }
            else
            {
                PluginLog.Warning("BehindResponse: locking the screen is not supported on this system");
                return;
            }

            PluginLog.Info("BehindResponse: locked the screen");
        }

        public static void ToggleMusic()
        {
            SendPlayPause?.Invoke();
            MusicPaused = !MusicPaused;
        }

        // Lowers the volume to DuckPct (never raises it); the second press restores it unless the user changed it meanwhile.
        public static void ToggleTalk()
        {
            var audio = FarVolume.Audio;
            if (!Talk)
            {
                var cur = audio.GetVolume();
                if (!cur.HasValue)
                {
                    PluginLog.Warning($"BehindResponse: cannot read the volume ({audio.Name}), talk mode needs it");
                    return;
                }

                var target = Math.Min(cur.Value, DuckPct);
                if (target != cur.Value && !audio.SetVolume(target))
                {
                    return;
                }

                _talkOrig = cur;
                _talkSet = target;
                Talk = true;
                PluginLog.Info($"BehindResponse: talk mode, volume {cur}% -> {target}%");
            }
            else
            {
                ReleaseTalk();
            }
        }

        public static void ReleaseTalk()
        {
            if (!Talk)
            {
                return;
            }

            var audio = FarVolume.Audio;
            var cur = audio.GetVolume();
            if (cur.HasValue && _talkSet.HasValue && _talkOrig.HasValue && Math.Abs(cur.Value - _talkSet.Value) <= 3)
            {
                audio.SetVolume(_talkOrig.Value);   // they did not touch it: put it back
            }

            Talk = false;
            _talkOrig = null;
            _talkSet = null;
            PluginLog.Info("BehindResponse: talk mode ended");
        }

        public static void Acknowledge() => PersonBehind.Acknowledge();

        public static void Snooze() => NotificationPolicy.Snooze(15);

        // Runs one of the named actions (Actions Ring, page, scripts). Returns false for an unknown name.
        public static Boolean Run(String action)
        {
            switch (action)
            {
                case "hide": Hide(); return true;
                case "back": Back(); return true;
                case "lock": Lock(); return true;
                case "music": ToggleMusic(); return true;
                case "talk": ToggleTalk(); return true;
                case "ok": Acknowledge(); return true;
                case "snooze": Snooze(); return true;
                default: return false;
            }
        }

        // "⌘ ⌥ H", "Ctrl ⇧ F5", "F11" -> key + modifiers. ⌘ is Command on a Mac and the Windows key on Windows.
        public static Boolean TryParseShortcut(String text, out VirtualKeyCode key, out ModifierKey mods)
        {
            key = VirtualKeyCode.None;
            mods = ModifierKey.None;
            if (String.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var haveKey = false;
            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (token)
                {
                    case "⌘":
                    case "Cmd":
                    case "Win":
                        mods |= OperatingSystem.IsMacOS() ? ModifierKey.Command : ModifierKey.Windows;
                        continue;
                    case "Ctrl":
                    case "Control":
                        mods |= ModifierKey.Control;
                        continue;
                    case "⌥":
                    case "Alt":
                    case "Option":
                        mods |= ModifierKey.Alt;
                        continue;
                    case "⇧":
                    case "Shift":
                        mods |= ModifierKey.Shift;
                        continue;
                }

                if (haveKey)
                {
                    return false;   // two normal keys: not a shortcut we can press
                }

                var name = token.Length == 1 && Char.IsLetter(token[0]) ? "Key" + Char.ToUpperInvariant(token[0])
                    : token.Length == 1 && Char.IsDigit(token[0]) ? "Key" + token
                    : token switch { "Enter" => "Return", "Backspace" => "Back", "Esc" => "Escape", _ => token };
                if (!Enum.TryParse(name, true, out VirtualKeyCode parsed) || parsed == VirtualKeyCode.None)
                {
                    return false;
                }

                key = parsed;
                haveKey = true;
            }

            return haveKey;
        }
    }
}
