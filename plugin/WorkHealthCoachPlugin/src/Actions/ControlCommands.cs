namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Slide controls: send arrow keys to the presentation app.
    public class SlideControlCommand : PluginDynamicCommand
    {
        public SlideControlCommand()
            : base()
        {
            this.AddParameter("next", "Next slide", "Coach widgets");
            this.AddParameter("prev", "Previous slide", "Coach widgets");
            this.AddParameter("reset", "Reset session", "Coach widgets");
        }

        protected override void RunCommand(String actionParameter)
        {
            switch (actionParameter)
            {
                case "next":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.ArrowRight, ModifierKey.None);
                    SessionState.NextSlide();
                    break;
                case "prev":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.ArrowLeft, ModifierKey.None);
                    SessionState.PrevSlide();
                    break;
                case "reset":
                    SessionState.Reset();
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => actionParameter switch
        {
            "next" => "Next >",
            "prev" => "< Prev",
            "reset" => "Reset",
            _ => null,
        };
    }
}

namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Keys that tune how loud the coach is. All three cycle through the options on each press.
    public class PolicyControlCommand : PluginDynamicCommand
    {
        public PolicyControlCommand()
            : base()
        {
            this.AddParameter("snooze", "Snooze alerts (15 min)", "Coach controls");
            this.AddParameter("preset", "Notification level (Quiet/Balanced/Attentive)", "Coach controls");
            this.AddParameter("strength", "Mouse buzz strength (Off/Light/Normal/Strong)", "Coach controls");
        }

        protected override Boolean OnLoad()
        {
            NotificationPolicy.Changed += this.Redraw;
            return true;
        }

        protected override Boolean OnUnload()
        {
            NotificationPolicy.Changed -= this.Redraw;
            return true;
        }

        private void Redraw() => this.ActionImageChanged();

        protected override void RunCommand(String actionParameter)
        {
            switch (actionParameter)
            {
                case "snooze":
                    NotificationPolicy.ToggleSnooze();
                    break;
                case "preset":
                    NotificationPolicy.CyclePreset();
                    break;
                case "strength":
                    NotificationPolicy.CycleStrength();
                    break;
            }
        }

        private static String Cap(String s) => String.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => actionParameter switch
        {
            "snooze" => NotificationPolicy.Snoozed
                ? $"Snoozed{Environment.NewLine}{Math.Ceiling((NotificationPolicy.SnoozeUntil - DateTime.UtcNow).TotalMinutes)} min"
                : $"Snooze{Environment.NewLine}15 min",
            "preset" => $"Level{Environment.NewLine}{Cap(NotificationPolicy.Settings.Preset)}",
            "strength" => $"Buzz{Environment.NewLine}{Cap(NotificationPolicy.Settings.HapticStrength)}",
            _ => null,
        };
    }
}
