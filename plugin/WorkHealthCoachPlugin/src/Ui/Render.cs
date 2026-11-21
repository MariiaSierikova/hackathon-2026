namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Shared drawing helpers for Keypad tiles.
    public static class Render
    {
        public static readonly BitmapColor Bg = new BitmapColor(18, 22, 30);
        public static readonly BitmapColor Dim = new BitmapColor(120, 130, 145);
        public static readonly BitmapColor Ok = new BitmapColor(76, 175, 120);
        public static readonly BitmapColor Warn = new BitmapColor(240, 170, 50);
        public static readonly BitmapColor Bad = new BitmapColor(230, 80, 70);
        public static readonly BitmapColor Accent = new BitmapColor(90, 150, 240);

        public static Int32 Px(PluginImageSize s) => s switch
        {
            PluginImageSize.Width90 => 80,
            _ => 116,
        };

        public static String Mmss(TimeSpan t)
        {
            var neg = t < TimeSpan.Zero;
            t = t.Duration();
            return $"{(neg ? "-" : String.Empty)}{(Int32)t.TotalMinutes:00}:{t.Seconds:00}";
        }

        // Standard tile: small caption on top, big value in the middle, optional footer.
        public static BitmapImage Tile(Int32 size, String caption, String value, BitmapColor color, String footer = null, BitmapColor? bg = null)
        {
            using var b = new BitmapBuilder(size, size);
            b.Clear(bg ?? Bg);
            b.DrawText(caption, 0, 4, size, size / 4, Dim, 12);
            b.DrawText(value, 0, size / 5, size, size * 3 / 5, color, size > 100 ? 30 : 24);
            if (footer != null)
            {
                b.DrawText(footer, 0, size * 4 / 5 - 2, size, size / 5, Dim, 11);
            }

            return b.ToImage();
        }
    }
}
