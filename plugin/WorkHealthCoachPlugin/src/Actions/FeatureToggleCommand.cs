namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;

    // One Options+ action per coaching feature (plus Master and Presenting mode). Drag them onto MX Keypad keys:
    // a press switches the feature on or off, and the key shows its pixel icon (dimmed when off).
    // The Pomodoro key also shows the live countdown while the timer runs, wherever it sits on the Keypad.
    // Search "Desk Coach" in Options+ -> All Actions.
    public class FeatureToggleCommand : PluginDynamicCommand
    {
        private const String Group = "Desk Coach";

        private static readonly (String Id, String Name)[] Features =
        {
            ("speed", "Speech speed"),
            ("volume", "Speech volume"),
            ("blink", "Eye blinking"),
            ("pomodoro", "Pomodoro"),
            ("distance", "Volume when far"),
            ("behind", "Person behind you"),
        };

        public FeatureToggleCommand()
            : base()
        {
            foreach (var (id, name) in Features)
            {
                this.AddParameter(id, name, Group);
            }

            this.AddParameter("master", "Master switch", Group);
            this.AddParameter("present", "Presenting mode (silent)", Group);
            this.AddParameter("alert", "Alert light (shows alerts, nothing else)", Group);
        }

        // Every key of this command is a "variation" (a parameter). The Logitech docs say ActionImageChanged() without a
        // parameter is for actions that have none; with parameters, say WHICH variation changed.
        // The cell (row, col) of the 3x3 alert picture that each key draws. Arrange the nine keys on the Keypad in this order:
        //   speed     volume    blink
        //   pomodoro  distance  behind
        //   master    present   alert
        public static readonly Dictionary<String, (Int32 Row, Int32 Col)> Cells = new()
        {
            ["speed"] = (0, 0), ["volume"] = (0, 1), ["blink"] = (0, 2),
            ["pomodoro"] = (1, 0), ["distance"] = (1, 1), ["behind"] = (1, 2),
            ["master"] = (2, 0), ["present"] = (2, 1), ["alert"] = (2, 2),
        };

        public static readonly String[] AllKeys = { "speed", "volume", "blink", "pomodoro", "distance", "behind", "master", "present", "alert" };

        private System.Timers.Timer _blinker;
        private Boolean _wasAlert;

        protected override Boolean OnLoad()
        {
            UiSettings.Changed += this.RedrawAll;
            PomodoroState.Changed += this.RedrawPomodoro;
            SessionState.Changed += this.RedrawIfAlert;

            // While an alert is showing, every Desk Coach key flashes the same alert; this timer animates it and puts the icons back afterwards.
            this._blinker = new System.Timers.Timer(400) { AutoReset = true };
            this._blinker.Elapsed += (_, _) => this.RedrawIfAlert();
            this._blinker.Start();
            return true;
        }

        protected override Boolean OnUnload()
        {
            UiSettings.Changed -= this.RedrawAll;
            PomodoroState.Changed -= this.RedrawPomodoro;
            SessionState.Changed -= this.RedrawIfAlert;
            this._blinker?.Stop();
            this._blinker?.Dispose();
            return true;
        }

        private void RedrawIfAlert()
        {
            if (SessionState.TakeoverActive || this._wasAlert)
            {
                this.RedrawAll();
            }
        }

        // A switch changed (page, key, profile, master): every key may look different, so refresh each one by name.
        private void RedrawAll()
        {
            foreach (var key in AllKeys)
            {
                this.ActionImageChanged(key);
            }
        }

        // The countdown only shows on the Pomodoro key, so only that key is redrawn every second.
        private void RedrawPomodoro() => this.ActionImageChanged("pomodoro");

        protected override void RunCommand(String actionParameter)
        {
            PluginLog.Info($"Desk Coach key pressed: '{actionParameter}'");

            // While an alert is showing on the keys, a press dismisses it (it must not flip a feature by accident).
            if (SessionState.TakeoverActive)
            {
                SessionState.DismissAlert();
                return;
            }

            switch (actionParameter)
            {
                case "master":
                    UiSettings.SetMaster(!UiSettings.Master);
                    break;
                case "present":
                    UiSettings.TogglePresenting();
                    break;
                case "alert":
                    break;
                default:
                    UiSettings.SetFeatureEnabled(actionParameter, !UiSettings.FeatureEnabled(actionParameter));
                    break;
            }
        }

        private static Boolean IsOn(String id) => id switch
        {
            null => false,
            "master" => UiSettings.Master,
            "present" => UiSettings.Profile == "presenting",
            _ => UiSettings.Master && UiSettings.FeatureEnabled(id),
        };

        // Normally no text on the key. During an alert a blank space overrides the key's default name, so it does not write over the picture.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => SessionState.TakeoverActive ? " " : String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                if (String.IsNullOrEmpty(actionParameter))
                {
                    PluginLog.Warning("Desk Coach key image requested without a key name (ignored)");
                    return null;
                }

                var px = Render.Px(imageSize);
                var alertImage = Cells.TryGetValue(actionParameter, out var cell) ? KeypadScreen.AlertTile(cell.Row, cell.Col, px) : null;
                this._wasAlert = alertImage != null;
                if (alertImage != null)
                {
                    return alertImage;
                }

                if (actionParameter == "alert")
                {
                    return Render.Tile(px, "DESK COACH", "OK", Render.Ok, "no alert");
                }

                var on = IsOn(actionParameter);
                PluginLog.Verbose($"Desk Coach key image: '{actionParameter}' {(on ? "ON" : "off")} {px}px");
                var icon = PluginResources.ReadImage($"feat_{actionParameter}{(on ? String.Empty : "_off")}.png");
                using var b = new BitmapBuilder(px, px);
                b.Clear(new BitmapColor(18, 22, 30));
                b.DrawImage(icon, 0, 0, px, px);

                if (actionParameter == "pomodoro" && PomodoroState.Active)
                {
                    var strip = px / 4;
                    b.FillRectangle(0, px - strip, px, strip, new BitmapColor(0, 0, 0));
                    var t = Render.Mmss(TimeSpan.FromSeconds(Math.Max(0, PomodoroState.LeftSec)));
                    var color = !PomodoroState.Running ? new BitmapColor(150, 160, 175)
                        : PomodoroState.Current == PomodoroState.Phase.Work ? new BitmapColor(240, 110, 100) : new BitmapColor(110, 215, 150);
                    b.DrawText(t, 0, px - strip, px, strip, color, px > 100 ? 22 : 18);
                }

                return b.ToImage();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"FeatureToggleCommand image failed for '{actionParameter}'");
                return null;
            }
        }
    }
}
