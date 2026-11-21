namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;

    // A picture drawn from big square "pixels" on a canvas that spans the whole 3x3 Keypad.
    // It is built once per frame and then sliced into the nine key images.
    public class PixelScene
    {
        private readonly record struct R(Int32 X, Int32 Y, Int32 W, Int32 H, BitmapColor C);

        private readonly List<R> _rects = new();
        private readonly BitmapColor _bg;

        public PixelScene(BitmapColor bg, Int32 size = 348)
        {
            this._bg = bg;
            this.Size = size;
        }

        public Int32 Size { get; }

        public void Rect(Int32 x, Int32 y, Int32 w, Int32 h, BitmapColor c) => this._rects.Add(new R(x, y, w, h, c));

        // Draws ASCII sprite rows ('.' = transparent) scaled up; neighbouring pixels of the same colour merge into one rectangle.
        public void Sprite(String[] rows, Dictionary<Char, (Byte r, Byte g, Byte b)> palette, Int32 ox, Int32 oy, Int32 scale)
        {
            for (var y = 0; y < rows.Length; y++)
            {
                var row = rows[y];
                var x = 0;
                while (x < row.Length)
                {
                    var ch = row[x];
                    if (ch == '.' || !palette.TryGetValue(ch, out var col))
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < row.Length && row[x] == ch)
                    {
                        x++;
                    }

                    this.Rect(ox + start * scale, oy + y * scale, (x - start) * scale, scale, new BitmapColor(col.r, col.g, col.b));
                }
            }
        }

        public void Sprite(String name, String frame, Int32 ox, Int32 oy, Int32 scale)
        {
            var (palette, frames) = SpriteData.All[name];
            this.Sprite(frames[frame], palette, ox, oy, scale);
        }

        public static Int32 SpriteWidth(String name, String frame) => SpriteData.All[name].frames[frame][0].Length;

        public static Int32 SpriteHeight(String name, String frame) => SpriteData.All[name].frames[frame].Length;

        // ---- chunky 3x5 pixel font ----
        private static readonly Dictionary<Char, String[]> Font = BuildFont();

        private static Dictionary<Char, String[]> BuildFont()
        {
            var f = new Dictionary<Char, String[]>();
            void G(Char c, String rows) => f[c] = rows.Split('/');
            G('A', "010/101/111/101/101"); G('B', "110/101/110/101/110"); G('C', "011/100/100/100/011");
            G('D', "110/101/101/101/110"); G('E', "111/100/110/100/111"); G('F', "111/100/110/100/100");
            G('G', "011/100/101/101/011"); G('H', "101/101/111/101/101"); G('I', "111/010/010/010/111");
            G('J', "001/001/001/101/010"); G('K', "101/101/110/101/101"); G('L', "100/100/100/100/111");
            G('M', "101/111/111/101/101"); G('N', "110/101/101/101/101"); G('O', "010/101/101/101/010");
            G('P', "110/101/110/100/100"); G('Q', "010/101/101/110/011"); G('R', "110/101/110/101/101");
            G('S', "011/100/010/001/110"); G('T', "111/010/010/010/010"); G('U', "101/101/101/101/111");
            G('V', "101/101/101/101/010"); G('W', "101/101/111/111/101"); G('X', "101/101/010/101/101");
            G('Y', "101/101/010/010/010"); G('Z', "111/001/010/100/111");
            G('0', "111/101/101/101/111"); G('1', "010/110/010/010/111"); G('2', "110/001/010/100/111");
            G('3', "110/001/010/001/110"); G('4', "101/101/111/001/001"); G('5', "111/100/110/001/110");
            G('6', "011/100/111/101/111"); G('7', "111/001/010/010/010"); G('8', "111/101/111/101/111");
            G('9', "111/101/111/001/110");
            G(':', "0/1/0/1/0"); G('!', "1/1/1/0/1"); G('-', "000/000/111/000/000"); G(' ', "00/00/00/00/00");
            return f;
        }

        public static Int32 TextWidth(String text, Int32 scale)
        {
            var w = 0;
            foreach (var ch in text.ToUpperInvariant())
            {
                w += (Font.TryGetValue(ch, out var g) ? g[0].Length : 3) + 1;
            }

            return Math.Max(0, w - 1) * scale;
        }

        public Int32 Text(String text, Int32 x, Int32 y, Int32 scale, BitmapColor color)
        {
            foreach (var ch in text.ToUpperInvariant())
            {
                if (!Font.TryGetValue(ch, out var g))
                {
                    x += 4 * scale;
                    continue;
                }

                for (var row = 0; row < 5; row++)
                {
                    var line = g[row];
                    var cx = 0;
                    while (cx < line.Length)
                    {
                        if (line[cx] != '1')
                        {
                            cx++;
                            continue;
                        }

                        var s = cx;
                        while (cx < line.Length && line[cx] == '1')
                        {
                            cx++;
                        }

                        this.Rect(x + s * scale, y + row * scale, (cx - s) * scale, scale, color);
                    }
                }

                x += (g[0].Length + 1) * scale;
            }

            return x;
        }

        // Text centred horizontally on the canvas.
        public void TextCentered(String text, Int32 y, Int32 scale, BitmapColor color) =>
            this.Text(text, (this.Size - TextWidth(text, scale)) / 2, y, scale, color);

        // ---- output ----
        // One key image: tile (row, col) of the 3x3 grid, scaled to `size` pixels.
        public BitmapImage Tile(Int32 row, Int32 col, Int32 size)
        {
            var t = this.Size / 3;
            var tx = col * t;
            var ty = row * t;
            var f = (Double)size / t;

            using var b = new BitmapBuilder(size, size);
            b.Clear(this._bg);
            foreach (var r in this._rects)
            {
                var x0 = Math.Max(r.X, tx);
                var y0 = Math.Max(r.Y, ty);
                var x1 = Math.Min(r.X + r.W, tx + t);
                var y1 = Math.Min(r.Y + r.H, ty + t);
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                var px = (Int32)Math.Floor((x0 - tx) * f);
                var py = (Int32)Math.Floor((y0 - ty) * f);
                var pw = (Int32)Math.Ceiling((x1 - tx) * f) - px;
                var ph = (Int32)Math.Ceiling((y1 - ty) * f) - py;
                b.FillRectangle(px, py, pw, ph, r.C);
            }

            return b.ToImage();
        }

        // The whole canvas as one image (used for the folder's entry key).
        public BitmapImage Whole(Int32 size)
        {
            var f = (Double)size / this.Size;
            using var b = new BitmapBuilder(size, size);
            b.Clear(this._bg);
            foreach (var r in this._rects)
            {
                var px = (Int32)Math.Floor(r.X * f);
                var py = (Int32)Math.Floor(r.Y * f);
                b.FillRectangle(px, py, (Int32)Math.Ceiling((r.X + r.W) * f) - px, (Int32)Math.Ceiling((r.Y + r.H) * f) - py, r.C);
            }

            return b.ToImage();
        }
    }
}
