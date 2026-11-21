namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Base class: redraws itself whenever SessionState.Changed fires.
    public abstract class WidgetCommand : PluginDynamicCommand
    {
        protected WidgetCommand(String name, String description)
            : base(displayName: name, description: description, groupName: "Coach widgets")
        {
        }

        protected override Boolean OnLoad()
        {
            SessionState.Changed += this.Redraw;
            return true;
        }

        protected override Boolean OnUnload()
        {
            SessionState.Changed -= this.Redraw;
            return true;
        }

        private void Redraw() => this.ActionImageChanged();

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => null;
    }

    // Presentation countdown. Press = start/pause.
    public class SessionTimerCommand : WidgetCommand
    {
        public SessionTimerCommand()
            : base("Session timer", "Time left in the presentation. Press to start/pause.")
        {
        }

        protected override void RunCommand(String actionParameter) => SessionState.StartPause();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var rem = SessionState.Remaining;
            var color = rem <= TimeSpan.FromMinutes(SessionState.WarnMinutes) ? Render.Bad
                : rem <= TimeSpan.FromMinutes(SessionState.WarnMinutes + 4) ? Render.Warn : Render.Ok;
            var footer = SessionState.Running ? "running" : "press to start";
            return Render.Tile(Render.Px(imageSize), "TIME LEFT", Render.Mmss(rem), color, footer);
        }
    }

    // Time on the current slide. Press = mark next slide (also use the Next slide key).
    public class SlideTimerCommand : WidgetCommand
    {
        public SlideTimerCommand()
            : base("Slide timer", "Time on the current slide. Press to count the next slide.")
        {
        }

        protected override void RunCommand(String actionParameter) => SessionState.NextSlide();

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            Render.Tile(Render.Px(imageSize), $"SLIDE {SessionState.SlideNumber}", Render.Mmss(SessionState.SlideElapsed), Render.Accent, "press = next");
    }

    // Pace meter. Fed by pace_too_fast triggers and by POST /metrics {"wpm":..}.
    public class PaceWidgetCommand : WidgetCommand
    {
        public PaceWidgetCommand()
            : base("Pace meter", "Speaking pace: green = fine, red = too fast.")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var wpm = SessionState.Wpm;
            var value = wpm.HasValue ? $"{(Int32)wpm.Value}" : "--";
            if (SessionState.PaceIsFast)
            {
                return Render.Tile(Render.Px(imageSize), "SLOW DOWN", value, BitmapColor.White, "words / min", new BitmapColor(150, 40, 40));
            }

            return Render.Tile(Render.Px(imageSize), "PACE", value, Render.Ok, "words / min");
        }
    }

    // A friendly eye that blinks when blink_low arrives.
    public class EyeWidgetCommand : WidgetCommand
    {
        public EyeWidgetCommand()
            : base("Blink eye", "An eye that blinks to remind you to blink.")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var s = Render.Px(imageSize);
            var open = SessionState.EyeOpenness;
            using var b = new BitmapBuilder(s, s);
            b.Clear(Render.Bg);

            var cx = s / 2;
            var cy = s / 2 - 4;
            var w = s - 24;
            var h = Math.Max(3, (Int32)(s * 0.52 * open));

            // eye "lens": white rounded block, iris + pupil when open enough
            b.FillRectangle(cx - w / 2, cy - h / 2, w, h, new BitmapColor(235, 240, 245));
            if (open > 0.4)
            {
                var iris = (Int32)(s * 0.30);
                b.FillRectangle(cx - iris / 2, cy - iris / 2, iris, iris, Render.Accent);
                var pupil = iris / 2;
                b.FillRectangle(cx - pupil / 2, cy - pupil / 2, pupil, pupil, new BitmapColor(10, 10, 20));
            }

            b.DrawText(SessionState.BlinkReminderActive ? "BLINK!" : "eyes ok", 0, s - s / 5 - 2, s, s / 5, SessionState.BlinkReminderActive ? Render.Warn : Render.Dim, 12);
            return b.ToImage();
        }
    }

    // Shows the latest alert for ~12 s, then goes calm again.
    public class AlertWidgetCommand : WidgetCommand
    {
        public AlertWidgetCommand()
            : base("Alert", "Shows the latest coaching alert. Press to dismiss it (two dismissals make the coach quieter).")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            SessionState.DismissAlert();
            NotificationPolicy.Dismiss();
        }

        private static (String text, BitmapColor color) Describe(String trigger) => trigger switch
        {
            "pace_too_fast" => ("SLOW\nDOWN", new BitmapColor(230, 140, 40)),
            "time_warning" => ("TIME\nWARNING", new BitmapColor(230, 80, 70)),
            "blink_low" => ("BLINK", new BitmapColor(90, 150, 240)),
            "eye_contact_lost" => ("LOOK UP", new BitmapColor(150, 110, 220)),
            "filler_burst" => ("FILLER\nWORDS", new BitmapColor(220, 120, 160)),
            "lookaway_done" => ("BREAK\nDONE", new BitmapColor(76, 175, 120)),
            "person_behind" => ("SOMEONE\nBEHIND", new BitmapColor(230, 60, 60)),
            _ => (trigger, new BitmapColor(120, 130, 145)),
        };

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var s = Render.Px(imageSize);
            using var b = new BitmapBuilder(s, s);
            if (SessionState.AlertActive)
            {
                var (text, color) = Describe(SessionState.LastTrigger);
                b.Clear(color);
                b.DrawText(text, 0, 0, s, s, BitmapColor.White, 18);
            }
            else
            {
                b.Clear(Render.Bg);
                b.DrawText("all calm", 0, 0, s, s, Render.Dim, 14);
            }

            return b.ToImage();
        }
    }

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
