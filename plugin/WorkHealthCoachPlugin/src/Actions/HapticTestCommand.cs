namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // One Keypad/Options+ action per haptic pattern, for testing the mouse without any camera.
    public class HapticTestCommand : PluginDynamicCommand
    {
        public HapticTestCommand() : base()
        {
            foreach (var name in HapticPlayer.PatternNames)
            {
                this.AddParameter(name, $"Buzz: {name}", "Coach / Haptic tests");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            PluginLog.Info($"Haptic test: {actionParameter}");
            HapticPlayer.Play(actionParameter);
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            actionParameter?.Replace('_', ' ');
    }
}
