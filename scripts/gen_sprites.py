#!/usr/bin/env python3
"""Draws the pixel-art sprites (eye, snail, owl, duck) and writes:
   assets/sprites.json                      (source of truth, used by the on-screen overlay)
   plugin/.../Ui/SpriteData.g.cs            (same data compiled into the Logi plugin)
   assets/sprites_preview.png               (contact sheet to look at)
Run:  uv run --with pillow python scripts/gen_sprites.py
"""
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


class Grid:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.g = [["."] * w for _ in range(h)]

    def set(self, x, y, c, only_empty=False):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < self.w and 0 <= y < self.h and (not only_empty or self.g[y][x] == "."):
            self.g[y][x] = c

    def get(self, x, y):
        return self.g[y][x] if 0 <= x < self.w and 0 <= y < self.h else "."

    def ellipse(self, cx, cy, rx, ry, c, only_over=None):
        for y in range(self.h):
            for x in range(self.w):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    if only_over is None or self.get(x, y) in only_over:
                        self.g[y][x] = c

    def line(self, x0, y0, x1, y1, c, thick=1):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for dx in range(thick):
                for dy in range(thick):
                    self.set(x + dx, y + dy, c)

    def outline(self, c, thick=1):
        for _ in range(thick):
            add = []
            for y in range(self.h):
                for x in range(self.w):
                    if self.g[y][x] == "." and any(self.get(x + dx, y + dy) != "."
                                                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                        add.append((x, y))
            for x, y in add:
                self.g[y][x] = c

    def rows(self):
        return ["".join(r) for r in self.g]


# ---------------------------------------------------------------- eye
EYE_PAL = {"k": (40, 36, 60), "w": (255, 255, 255), "b": (120, 180, 255), "n": (70, 120, 220),
           "d": (22, 22, 48), "p": (255, 150, 175)}


def eye(o):
    W, H = 46, 32
    g = Grid(W, H)
    cx, cy, rx = (W - 1) / 2, (H - 1) / 2 + 2, 17
    if o > 0.2:
        hh = 9.5 * o
        for y in range(H):
            for x in range(W):
                dx = (x - cx) / rx
                if abs(dx) <= 1 and abs(y - cy) <= hh * math.sqrt(1 - dx * dx):
                    g.set(x, y, "w")
        for y in range(H):
            for x in range(W):
                d = math.hypot(x - cx, y - cy)
                if d <= 7.6 and g.get(x, y) == "w":
                    g.set(x, y, "n" if d > 5.9 else "b")
        g.ellipse(cx, cy, 3.3, 3.3, "d", only_over=("b", "n"))
        for sx, sy in ((cx - 3, cy - 3), (cx - 2, cy - 3), (cx - 3, cy - 2), (cx - 2, cy - 2),
                       (cx + 2, cy + 2), (cx + 3, cy + 2)):
            g.set(sx, sy, "w")
        g.outline("k", 3)
        for side in (-1, 1):                                  # three fanned lashes per side
            bx = cx + side * (rx - 1)
            for (dx0, dy0, ex, ey) in ((0, -2, 6, -8), (-3, -5, 3, -12), (-6, -7, -1, -14)):
                g.line(bx + side * dx0, cy + dy0 - 3, bx + side * ex, cy + ey - 3, "k", 2)
    else:                                                     # happy closed eye
        for x in range(2, W - 2):
            dx = (x - cx) / (rx + 1)
            if abs(dx) <= 1:
                y = cy + 3 - 6 * (dx * dx)
                for k in (-1, 0, 1, 2):
                    g.set(x, y + k, "k")
        for side in (-1, 1):
            bx = cx + side * (rx - 1)
            for (dx0, dy0, ex, ey) in ((0, -2, 6, -8), (-3, -5, 3, -12), (-6, -7, -1, -14)):
                g.line(bx + side * dx0, cy + dy0 - 1, bx + side * ex, cy + ey - 1, "k", 2)
    return g.rows()


# ---------------------------------------------------------------- snail
SNAIL_PAL = {"k": (60, 40, 40), "o": (250, 175, 70), "s": (214, 120, 50), "g": (190, 225, 150),
             "w": (255, 255, 255), "d": (30, 25, 45), "p": (255, 150, 170)}


def snail(frame):
    W, H = 40, 26
    g = Grid(W, H)
    g.ellipse(18, 21, 16.5, 3.6, "g")                     # foot / body
    g.ellipse(31, 16, 5.6, 5.4, "g")                      # head
    g.ellipse(17, 11.5, 10.5, 10, "o")                    # shell
    for y in range(H):                                    # spiral
        for x in range(W):
            d = math.hypot(x - 17, y - 11.5)
            ang = math.atan2(y - 11.5, x - 17)
            if g.get(x, y) == "o" and (abs(d - 7.4) < 0.75 or abs(d - 3.9) < 0.75) and not (-0.4 < ang < 0.3):
                g.set(x, y, "s")
    g.set(17, 11, "s"); g.set(18, 11, "s")
    sway = 0 if frame == 0 else 1
    for ex, tip in ((29, 27 + sway), (34, 36 - sway)):    # eye stalks
        g.line(ex, 12, tip, 5, "g", 2)
        g.ellipse(tip + 0.5, 4, 2.4, 2.4, "w")
        g.set(tip + 1, 4, "d"); g.set(tip + 1, 5, "d")
    g.set(33, 18, "d"); g.set(34, 19, "d"); g.set(35, 19, "d"); g.set(36, 18, "d")   # smile
    g.ellipse(29.5, 17.5, 1.5, 1.0, "p")                  # blush
    g.outline("k", 1)
    return g.rows()


# ---------------------------------------------------------------- owl
OWL_PAL = {"k": (50, 36, 40), "r": (160, 108, 74), "R": (120, 78, 54), "c": (245, 224, 180),
           "w": (255, 255, 255), "d": (28, 24, 44), "y": (255, 176, 60), "p": (255, 150, 170)}


def owl(o):
    W, H = 34, 36
    g = Grid(W, H)
    g.ellipse(16.5, 22, 13, 12.5, "r")                    # body
    g.ellipse(16.5, 14, 13.5, 10, "r")                    # head
    for side in (-1, 1):                                  # ear tufts
        bx = 16.5 + side * 9
        for i in range(7):
            for j in range(0, 7 - i):
                g.set(bx + side * j * 0.8 - side * 1.5, 3 + i, "r")
    g.ellipse(16.5, 26, 8, 8.5, "c")                      # belly
    for fy in (23, 27, 31):                               # feather marks
        for fx in (11, 16, 22):
            g.set(fx, fy, "r"); g.set(fx + 1, fy + 1, "r"); g.set(fx - 1, fy + 1, "r")
    for side in (-1, 1):                                  # wings
        g.ellipse(16.5 + side * 13, 24, 3.2, 8, "R")
    for side in (-1, 1):                                  # eyes
        ex = 16.5 + side * 6.2
        if o > 0.5:
            g.ellipse(ex, 14, 5.2, 5.2, "w")
            g.ellipse(ex, 14.5, 2.6, 2.6, "d")
            g.set(ex - 1, 13, "w"); g.set(ex, 13, "w")
        else:
            for x in range(-5, 6):
                y = 15 + (x * x) / 8 * -1 + 2
                g.set(ex + x, y, "d")
                g.set(ex + x, y + 1, "d")
    for i in range(3):                                    # beak
        for j in range(3 - i):
            g.set(15.5 + j + i * 0, 18 + i, "y")
    g.set(16, 18, "y"); g.set(17, 18, "y"); g.set(16, 19, "y"); g.set(17, 19, "y")
    g.ellipse(7, 19, 1.8, 1.1, "p"); g.ellipse(26, 19, 1.8, 1.1, "p")   # blush
    for fx in (12, 21):                                   # feet
        for k in range(3):
            g.set(fx + k * 2 - 1, 34, "y"); g.set(fx + k * 2 - 1, 33, "y")
    g.outline("k", 1)
    return g.rows()


# ---------------------------------------------------------------- duck (speak up!)
DUCK_PAL = {"k": (70, 50, 30), "y": (255, 214, 64), "Y": (238, 176, 40), "o": (255, 138, 40), "d": (30, 25, 45),
            "w": (255, 255, 255), "p": (255, 150, 170), "m": (150, 40, 55)}


def duck(open_mouth):
    W, H = 40, 36
    g = Grid(W, H)
    g.ellipse(14, 25, 12.5, 8.5, "y")                     # body
    for i in range(6):                                    # tail feathers (left)
        for j in range(6 - i):
            g.set(2 + j, 17 + i, "y")
    g.ellipse(23, 12, 8.5, 8.5, "y")                      # head
    g.ellipse(13, 26, 6.5, 4.0, "Y")                      # wing
    g.set(23, 4, "y"); g.set(24, 3, "y"); g.set(22, 3, "y")   # little head tuft
    g.set(24, 10, "d"); g.set(25, 10, "d"); g.set(24, 11, "d"); g.set(25, 11, "d")   # eye
    g.set(24, 10, "w")
    g.ellipse(22.5, 14.5, 1.7, 1.1, "p")                  # blush
    if open_mouth:
        g.ellipse(33.5, 11.5, 5, 1.8, "o")                # upper beak
        g.ellipse(32.5, 17.0, 4.2, 1.7, "o")              # lower beak
        for x in range(29, 36):
            for y in range(13, 16):
                g.set(x, y, "m")                          # open mouth
    else:
        g.ellipse(33.0, 14.5, 5, 2.4, "o")                # closed beak
    for fx in (9, 17):                                    # feet
        g.line(fx, 32, fx, 34, "o", 1)
        g.line(fx - 2, 34, fx + 2, 34, "o", 2)
    g.outline("k", 1)
    return g.rows()


SPRITES = {
    "eye": {"palette": EYE_PAL, "frames": {"open": eye(1.0), "half": eye(0.5), "closed": eye(0.0)}},
    "snail": {"palette": SNAIL_PAL, "frames": {"a": snail(0), "b": snail(1)}},
    "owl": {"palette": OWL_PAL, "frames": {"open": owl(1.0), "closed": owl(0.0)}},
    "duck": {"palette": DUCK_PAL, "frames": {"closed": duck(False), "open": duck(True)}},
}


def write_all():
    data = {n: {"palette": {k: list(v) for k, v in s["palette"].items()}, "frames": s["frames"]}
            for n, s in SPRITES.items()}
    (ROOT / "assets" / "sprites.json").write_text(json.dumps(data, indent=1))

    cs = ["// <auto-generated> by scripts/gen_sprites.py - do not edit. </auto-generated>",
          "namespace Loupedeck.WorkHealthCoachPlugin", "{", "    using System.Collections.Generic;", "",
          "    public static partial class SpriteData", "    {",
          "        public static readonly Dictionary<string, (Dictionary<char, (byte r, byte g, byte b)> palette, Dictionary<string, string[]> frames)> All = new()",
          "        {"]
    for n, s in SPRITES.items():
        pal = ", ".join(f"['{k}'] = ({v[0]}, {v[1]}, {v[2]})" for k, v in s["palette"].items())
        frames = ",\n                    ".join(
            f'["{fn}"] = new[] {{ ' + ", ".join(f'"{r}"' for r in rows) + " }" for fn, rows in s["frames"].items())
        cs.append(f'            ["{n}"] = (new() {{ {pal} }},\n                new()\n                {{\n                    {frames},\n                }}),')
    cs += ["        };", "    }", "}"]
    out = ROOT / "plugin/WorkHealthCoachPlugin/src/Ui/SpriteData.g.cs"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(cs) + "\n")


def preview():
    from PIL import Image
    scale, pad = 10, 14
    items = [(n, fn, rows, s["palette"]) for n, s in SPRITES.items() for fn, rows in s["frames"].items()]
    cw = max(len(r[0]) for _, _, r, _ in items) * scale + pad * 2
    ch = max(len(r) for _, _, r, _ in items) * scale + pad * 2
    cols = 3
    rws = (len(items) + cols - 1) // cols
    for bg_name, bg in (("light", (245, 245, 247)), ("dark", (24, 28, 38))):
        sheet = Image.new("RGB", (cw * cols, ch * rws), bg)
        for i, (n, fn, rows, pal) in enumerate(items):
            ox, oy = (i % cols) * cw + pad, (i // cols) * ch + pad
            for y, row in enumerate(rows):
                for x, c in enumerate(row):
                    if c != ".":
                        sheet.paste(tuple(pal[c]), (ox + x * scale, oy + y * scale, ox + (x + 1) * scale, oy + (y + 1) * scale))
        sheet.save(ROOT / "assets" / f"sprites_preview_{bg_name}.png")


if __name__ == "__main__":
    write_all()
    try:
        preview()
        print("wrote sprites.json, SpriteData.g.cs and preview PNGs")
    except ImportError:
        print("wrote sprites.json and SpriteData.g.cs (install pillow for the preview)")
