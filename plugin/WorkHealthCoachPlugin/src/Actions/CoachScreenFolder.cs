namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;

    // "Coach screen": one Keypad key opens a folder that owns all nine keys as a single canvas.
    // Normally a big readable control panel; the full-Keypad reminders appear on it only when needed.
    public class CoachScreenFolder : PluginDynamicFolder
    {
        private static readonly String[] Slots = { "00", "01", "02", "10", "11", "12", "20", "21", "22" };

        private PixelScene _scene = KeypadScreen.Build();

        public CoachScreenFolder()
        {
            this.DisplayName = "Coach screen";
            this.Description = "Big timer and slide controls. Blink, pace and time reminders take over the whole Keypad only when needed.";
            this.GroupName = "Coach";
        }

        public override Boolean Load()
        {
            KeypadScreen.Start();
            KeypadScreen.Changed += this.OnChanged;
            return true;
        }

        public override Boolean Unload()
        {
            KeypadScreen.Changed -= this.OnChanged;
            KeypadScreen.Stop();
            return true;
        }

        private void OnChanged()
        {
            try
            {
                this._scene = KeypadScreen.Build();   // all nine keys come from the same snapshot
                foreach (var slot in Slots)
                {
                    this.CommandImageChanged(slot);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "CoachScreenFolder redraw failed");
            }
        }

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.EncoderArea;

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType deviceType)
        {
            var names = new List<String>();
            foreach (var s in Slots)
            {
                names.Add(this.CreateCommandName(s));
            }

            return names;
        }

        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => String.Empty;

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            try
            {
                var key = Key(actionParameter);
                return this._scene.Tile(key[0] - '0', key[1] - '0', Render.Px(imageSize));
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "CoachScreenFolder image failed");
                return null;
            }
        }

        public override String GetButtonDisplayName(PluginImageSize imageSize) => String.Empty;

        public override BitmapImage GetButtonImage(PluginImageSize imageSize)
        {
            var s = new PixelScene(new BitmapColor(255, 246, 232), 116);
            s.Sprite("eye", "open", 6, 38, 2);
            return s.Whole(Render.Px(imageSize));
        }

        public override void RunCommand(String actionParameter)
        {
            var key = Key(actionParameter);

            if (SessionState.TakeoverActive)
            {
                // Any key press waves the reminder away (counts toward the coach getting quieter).
                SessionState.DismissAlert();
                NotificationPolicy.Dismiss();
                return;
            }

            switch (key)
            {
                case "10":
                case "11":
                case "12":
                case "21":
                    SessionState.StartPause();
                    break;
                case "20":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.ArrowLeft, ModifierKey.None);
                    SessionState.PrevSlide();
                    break;
                case "22":
                    this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.ArrowRight, ModifierKey.None);
                    SessionState.NextSlide();
                    break;
            }
        }

        private static String Key(String actionParameter) =>
            actionParameter.Length >= 2 ? actionParameter.Substring(actionParameter.Length - 2) : actionParameter;
    }
}
