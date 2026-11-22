namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Quick responses for "someone is coming to my desk". Meant for the MX Master 4 Actions Ring (the round on-screen menu
    // next to the cursor) and for Keypad keys: in Options+ assign them under "Desk Coach: person behind".
    // With headphones on you feel the buzz, glance at the Keypad, then pick one with the mouse, no keyboard needed.
    public class PersonBehindCommand : PluginDynamicCommand
    {
        private const String Group = "Desk Coach: person behind";

        public PersonBehindCommand()
            : base()
        {
            this.AddParameter("hide", "Hide my screen", Group);
            this.AddParameter("back", "Show my screen again", Group);
            this.AddParameter("music", "Pause / resume music", Group);
            this.AddParameter("talk", "Talk mode (lower music)", Group);
            this.AddParameter("ok", "It is fine (dismiss)", Group);
            this.AddParameter("snooze", "Snooze alerts 15 min", Group);
        }

        protected override void RunCommand(String actionParameter)
        {
            PluginLog.Info($"Person-behind action: {actionParameter}");
            BehindResponse.Run(actionParameter);
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => actionParameter switch
        {
            "hide" => $"Hide{Environment.NewLine}screen",
            "back" => $"Show{Environment.NewLine}screen",
            "music" => BehindResponse.MusicPaused ? $"Resume{Environment.NewLine}music" : $"Pause{Environment.NewLine}music",
            "talk" => BehindResponse.Talk ? $"End talk{Environment.NewLine}mode" : $"Talk{Environment.NewLine}mode",
            "ok" => $"It is{Environment.NewLine}fine",
            "snooze" => $"Snooze{Environment.NewLine}15 min",
            _ => null,
        };
    }
}
