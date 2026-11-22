namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Decides what the whole 3x3 Keypad shows and draws it.
    //   idle: slide info + HUGE session timer + prev / play-pause / next
    //   only when needed, a full-Keypad takeover: blinking eye (until you blink), crawling snail (too fast), time and "someone behind" warnings
    public static class KeypadScreen
    {
        private static readonly BitmapColor Dark = new(18, 22, 30);
        private static readonly BitmapColor Panel = new(36, 44, 58);
        private static readonly BitmapColor Cream = new(255, 246, 232);
        private static readonly BitmapColor Mint = new(222, 243, 230);
        private static readonly BitmapColor InkSoft = new(70, 56, 100);
        private static readonly BitmapColor White = new(250, 250, 252);

        private static System.Threading.Timer _timer;
        private static Int32 _lastSecond = -1;

        public static event Action Changed;

        public static void Start()
        {
            _timer = new System.Threading.Timer(_ => Pulse(), null, 200, 150);
            SessionState.Changed += () => Changed?.Invoke();
            PomodoroState.Changed += Forward;
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            PomodoroState.Changed -= Forward;
        }

        private static void Forward() => Changed?.Invoke();

        private static void Pulse()
        {
            var animating = SessionState.TakeoverActive;
            var second = PomodoroState.Active ? PomodoroState.LeftSec : (Int32)SessionState.Elapsed.TotalSeconds;
            if (animating || second != _lastSecond)
            {
                _lastSecond = second;
                Changed?.Invoke();
            }
        }

        public static PixelScene Build()
        {
            if (SessionState.BehindActive)
            {
                return Behind();
            }

            if (SessionState.TimeAlertActive)
            {
                return TimeWarning();
            }

            if (SessionState.GenericActive)
            {
                return Generic();
            }

            if (SessionState.EyeAlertActive)
            {
                return EyeReminder();
            }

            if (SessionState.PaceAlertActive)
            {
                return Snail();
            }

            return Idle();
        }

        // ------------------------------------------------------------ takeover scenes
        private static PixelScene EyeReminder()
        {
            var s = new PixelScene(Cream);
            var ms = (Int32)((DateTime.UtcNow - SessionState.EyeAlertStart).TotalMilliseconds % 1800);
            var frame = ms < 1000 ? "open" : ms < 1130 ? "half" : ms < 1330 ? "closed" : ms < 1460 ? "half" : "open";
            const Int32 scale = 7;
            var w = PixelScene.SpriteWidth("eye", "open") * scale;
            var h = PixelScene.SpriteHeight("eye", "open") * scale;
            s.Sprite("eye", frame, (s.Size - w) / 2, 40, scale);
            _ = h;
            s.TextCentered("BLINK!", 280, 9, InkSoft);
            return s;
        }

        private static PixelScene Snail()
        {
            var s = new PixelScene(Mint);
            s.TextCentered("SLOW DOWN", 24, 9, InkSoft);
            var p = Math.Min(1.0, (DateTime.UtcNow - SessionState.PaceAlertStart).TotalSeconds / 14.0);
            const Int32 scale = 5;
            var w = PixelScene.SpriteWidth("snail", "a") * scale;
            var h = PixelScene.SpriteHeight("snail", "a") * scale;
            var x = (Int32)(4 + p * (s.Size - w - 8));
            var frame = (Int32)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 450 % 2) == 0 ? "a" : "b";
            s.Sprite("snail", frame, x, s.Size - h - 18, scale);
            s.Rect(0, s.Size - 12, s.Size, 12, new BitmapColor(190, 225, 190));   // grass
            return s;
        }

        private static PixelScene TimeWarning()
        {
            var flash = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 500 % 2) == 0;
            var s = new PixelScene(flash ? new BitmapColor(170, 40, 40) : new BitmapColor(110, 24, 24));
            s.TextCentered("TIME", 36, 14, White);
            s.TextCentered(Render.Mmss(SessionState.Remaining < TimeSpan.Zero ? TimeSpan.Zero : SessionState.Remaining), 150, 14, White);
            s.TextCentered("LEFT", 262, 10, new BitmapColor(255, 200, 200));
            return s;
        }

        private static PixelScene Behind()
        {
            var flash = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 400 % 2) == 0;
            var s = new PixelScene(flash ? new BitmapColor(190, 40, 40) : new BitmapColor(120, 24, 24));
            s.TextCentered("SOMEONE", 60, 12, White);
            s.TextCentered("BEHIND", 170, 12, White);
            s.TextCentered("YOU", 270, 9, new BitmapColor(255, 210, 210));
            return s;
        }

        // ------------------------------------------------------------ idle control panel
        private static PixelScene Generic()
        {
            var (r, g, b) = SessionState.GenericColor;
            var flash = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 500 % 2) == 0;
            var s = new PixelScene(flash ? new BitmapColor(r, g, b) : new BitmapColor((Int32)(r * 0.65), (Int32)(g * 0.65), (Int32)(b * 0.65)));
            var lines = SessionState.GenericLines;
            if (lines.Length > 0)
            {
                s.TextCentered(lines[0], lines.Length > 1 ? 80 : 150, 12, White);
            }

            if (lines.Length > 1)
            {
                s.TextCentered(lines[1], 190, 12, White);
            }

            return s;
        }

        // Pomodoro timer in the same layout as the idle screen: label on top, huge MM:SS, reset / play-pause / skip below.
        private static PixelScene PomodoroIdle()
        {
            var s = new PixelScene(Dark);
            var work = PomodoroState.Current == PomodoroState.Phase.Work;
            var away = work && PomodoroState.UsePresence && !PomodoroState.AtDesk;
            var name = work ? $"FOCUS {PomodoroState.Round} OF {PomodoroState.Rounds}"
                : PomodoroState.Current == PomodoroState.Phase.Long ? "LONG BREAK" : "SHORT BREAK";
            var state = away ? "AWAY" : !PomodoroState.Running ? "PAUSED" : work ? "AT DESK" : "REST";
            var grey = new BitmapColor(120, 130, 150);
            var color = away || !PomodoroState.Running ? grey
                : work ? (PomodoroState.LeftSec <= 60 ? new BitmapColor(245, 180, 60) : new BitmapColor(240, 110, 100))
                : new BitmapColor(110, 215, 150);

            s.Text(name, 14, 14, 5, new BitmapColor(130, 150, 190));
            s.Text(state, 14, 58, 7, color);
            s.TextCentered(Render.Mmss(TimeSpan.FromSeconds(Math.Max(0, PomodoroState.LeftSec))), 124, 20, color);

            for (var i = 0; i < 3; i++)
            {
                s.Rect(i * 116 + 8, 232 + 8, 100, 100, Panel);
            }

            var icon = new BitmapColor(235, 240, 250);
            s.Text("RESET", 58 - PixelScene.TextWidth("RESET", 4) / 2, 280, 4, icon);
            if (PomodoroState.Running)
            {
                s.Rect(116 + 58 - 22, 290 - 30, 14, 60, icon);
                s.Rect(116 + 58 + 8, 290 - 30, 14, 60, icon);
            }
            else
            {
                Arrow(s, 116 + 58 + 4, 290, left: false, new BitmapColor(110, 215, 150));
            }

            Arrow(s, 232 + 58 - 8, 290, left: false, icon);
            s.Rect(232 + 58 + 26, 290 - 30, 8, 60, icon);
            return s;
        }

        private static PixelScene Idle()
        {
            if (PomodoroState.Active)
            {
                return PomodoroIdle();
            }

            var s = new PixelScene(Dark);

            // top row: slide + pace
            s.Text($"SLIDE {SessionState.SlideNumber}", 14, 12, 7, new BitmapColor(130, 150, 190));
            s.Text(Render.Mmss(SessionState.SlideElapsed), 14, 62, 8, new BitmapColor(110, 170, 250));
            if (SessionState.Wpm.HasValue)
            {
                var v = ((Int32)SessionState.Wpm.Value).ToString();
                s.Text(v, 348 - PixelScene.TextWidth(v, 8) - 14, 12, 8, SessionState.PaceIsFast ? new BitmapColor(240, 110, 90) : new BitmapColor(110, 200, 140));
                s.Text("WPM", 348 - PixelScene.TextWidth("WPM", 5) - 14, 70, 5, new BitmapColor(130, 150, 190));
            }

            // middle row: huge session timer
            var rem = SessionState.Remaining;
            var color = !SessionState.Running ? new BitmapColor(120, 130, 150)
                : rem <= TimeSpan.FromMinutes(SessionState.WarnMinutes) ? new BitmapColor(240, 90, 80)
                : rem <= TimeSpan.FromMinutes(SessionState.WarnMinutes + 4) ? new BitmapColor(245, 180, 60)
                : new BitmapColor(110, 215, 150);
            var text = Render.Mmss(rem < TimeSpan.Zero ? TimeSpan.Zero : rem);
            s.TextCentered(text, 124, 20, color);

            // bottom row: three big buttons
            for (var i = 0; i < 3; i++)
            {
                s.Rect(i * 116 + 8, 232 + 8, 100, 100, Panel);
            }

            var icon = new BitmapColor(235, 240, 250);
            Arrow(s, 116 * 0 + 58, 290, left: true, icon);
            if (SessionState.Running)
            {
                s.Rect(116 + 58 - 22, 290 - 30, 14, 60, icon);
                s.Rect(116 + 58 + 8, 290 - 30, 14, 60, icon);
            }
            else
            {
                Arrow(s, 116 + 58 + 4, 290, left: false, new BitmapColor(110, 215, 150));
            }

            Arrow(s, 232 + 58, 290, left: false, icon);
            return s;
        }

        // Pixel-art triangle arrow centred at (cx, cy).
        private static void Arrow(PixelScene s, Int32 cx, Int32 cy, Boolean left, BitmapColor c)
        {
            const Int32 u = 7;           // pixel size
            const Int32 rows = 9;
            for (var i = 0; i < rows; i++)
            {
                var len = i <= rows / 2 ? i + 1 : rows - i;
                var y = cy - rows * u / 2 + i * u;
                var w = len * u;
                s.Rect(left ? cx + 14 - w + 0 : cx - 14, y, w, u, c);
            }
        }
    }
}
