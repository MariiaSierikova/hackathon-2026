namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

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
        }

        // Every key of this command is a "variation" (a parameter). The Logitech docs say ActionImageChanged() without a
        // parameter is for actions that have none; with parameters, say WHICH variation changed.
        public static readonly String[] AllKeys = { "speed", "volume", "blink", "pomodoro", "distance", "behind", "master", "present" };

        protected override Boolean OnLoad()
        {
            UiSettings.Changed += this.RedrawAll;
            PomodoroState.Changed += this.RedrawPomodoro;
            return true;
        }

        protected override Boolean OnUnload()
        {
            UiSettings.Changed -= this.RedrawAll;
            PomodoroState.Changed -= this.RedrawPomodoro;
            return true;
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
            switch (actionParameter)
            {
                case "master":
                    UiSettings.SetMaster(!UiSettings.Master);
                    break;
                case "present":
                    UiSettings.TogglePresenting();
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

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

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
