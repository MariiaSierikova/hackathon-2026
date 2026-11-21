#!/usr/bin/env python3
"""On-screen pixel animals for the presenter: a snail crawls across the bottom when you speak too fast,
an owl blinks in the corner until you blink. Click-through, always on top, and hidden from screen sharing.

  uv run --project brain python overlay/overlay.py            # follow the Logi plugin (normal use)
  uv run --project brain python overlay/overlay.py --demo owl # show an animal without the plugin
  uv run --project brain python overlay/overlay.py --demo snail
"""
import argparse
import ctypes
import json
import sys
import threading
import time
import urllib.request
from pathlib import Path

from PySide6 import QtCore, QtGui, QtWidgets

ROOT = Path(__file__).resolve().parent.parent
SPRITES = json.loads((ROOT / "assets" / "sprites.json").read_text())
API = "http://127.0.0.1:8787"
SCALE = 6
HEIGHT = 270


def say(msg):
    """Console log with a timestamp, so we can see what the overlay saw and did."""
    print(time.strftime("[overlay %H:%M:%S] ") + msg, flush=True)


def sprite_pixmap(name, frame, scale=SCALE):
    pal = {k: QtGui.QColor(*v) for k, v in SPRITES[name]["palette"].items()}
    rows = SPRITES[name]["frames"][frame]
    img = QtGui.QImage(len(rows[0]), len(rows), QtGui.QImage.Format_ARGB32)
    img.fill(QtCore.Qt.transparent)
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c != ".":
                img.setPixelColor(x, y, pal[c])
    return QtGui.QPixmap.fromImage(img.scaled(img.width() * scale, img.height() * scale,
                                              QtCore.Qt.IgnoreAspectRatio, QtCore.Qt.FastTransformation))


def exclude_from_capture(widget):
    """Hide this window from screenshots, recordings and screen sharing (best effort, see docs/OVERLAY.md)."""
    try:
        wid = int(widget.winId())
        if sys.platform == "darwin":
            import objc
            view = objc.objc_object(c_void_p=ctypes.c_void_p(wid))
            win = view.window()
            win.setSharingType_(0)                      # NSWindowSharingNone
            win.setLevel_(25)                           # NSStatusWindowLevel: above normal and most full-screen windows
            win.setCollectionBehavior_(1 | 16 | 256)    # all spaces | stationary | full-screen auxiliary
            win.setIgnoresMouseEvents_(True)
        elif sys.platform.startswith("win"):
            ctypes.windll.user32.SetWindowDisplayAffinity(wid, 0x11)   # WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+)
    except Exception as e:  # never crash the overlay over this
        print("could not exclude from capture:", e, file=sys.stderr)


class Overlay(QtWidgets.QWidget):
    def __init__(self, demo=None):
        super().__init__()
        self.setWindowFlags(QtCore.Qt.FramelessWindowHint | QtCore.Qt.WindowStaysOnTopHint | QtCore.Qt.Tool
                            | QtCore.Qt.WindowTransparentForInput | QtCore.Qt.NoDropShadowWindowHint)
        self.setAttribute(QtCore.Qt.WA_TranslucentBackground)
        self.setAttribute(QtCore.Qt.WA_ShowWithoutActivating)
        self.setAttribute(QtCore.Qt.WA_TransparentForMouseEvents)
        g = QtGui.QGuiApplication.primaryScreen().availableGeometry()
        self.setGeometry(g.x(), g.bottom() - HEIGHT + 1, g.width(), HEIGHT)

        self.animal, self.t0 = None, 0.0
        self.plugin_down = False
        self.demo = demo
        self.excluded = False
        self.cache = {}
        self.timer = QtCore.QTimer(self, interval=33, timeout=self.tick)
        self.timer.start()
        threading.Thread(target=self.poll, daemon=True).start()

    # ---- state from the plugin (or demo) ----
    def poll(self):
        while True:
            try:
                if self.demo:
                    cycle = (time.time() % 16)
                    self.animal, self.t0 = (self.demo, time.time() - cycle) if cycle < 14 else (None, 0.0)
                else:
                    with urllib.request.urlopen(API + "/overlay", timeout=1) as r:
                        d = json.loads(r.read())
                    if d["animal"] != self.animal:
                        self.t0 = time.time() - d["ageMs"] / 1000.0
                        say(f"plugin says animal: {self.animal} -> {d['animal']}")
                    self.animal = d["animal"]
                    if self.plugin_down:
                        say("plugin reachable again")
                        self.plugin_down = False
            except Exception as e:
                if not self.plugin_down:
                    say(f"plugin NOT reachable ({e}); hiding. Is Logi Options+ running with the plugin?")
                    self.plugin_down = True
                self.animal = None          # plugin not running: stay invisible
            time.sleep(0.25)

    def pix(self, name, frame):
        key = (name, frame)
        if key not in self.cache:
            self.cache[key] = sprite_pixmap(name, frame)
        return self.cache[key]

    # ---- show / hide (a hidden window gets no paint events, so this must run from the timer) ----
    def tick(self):
        if self.animal is None:
            if self.isVisible():
                self.hide()
                say("window hidden")
            return
        if not self.isVisible():
            self.show()
            self.raise_()
            say(f"window shown ({self.animal})")
        if not self.excluded:
            exclude_from_capture(self)
            self.excluded = True
        self.update()

    # ---- drawing ----
    def paintEvent(self, _):
        animal = self.animal
        if animal is None:
            return

        age = time.time() - self.t0
        p = QtGui.QPainter(self)
        p.setRenderHint(QtGui.QPainter.Antialiasing, False)
        p.setOpacity(min(1.0, age / 0.4))                               # fade in
        w, h = self.width(), self.height()
        if animal == "snail":
            frame = "a" if int(age * 1000 / 450) % 2 == 0 else "b"
            pm = self.pix("snail", frame)
            progress = min(1.0, age / 14.0)
            x = int(20 + progress * (w - pm.width() - 40))
            p.drawPixmap(x, h - pm.height() - 12, pm)
            self.bubble(p, "slow down!", x + pm.width() // 2, h - pm.height() - 30)
        elif animal == "owl":
            ms = int(age * 1000) % 3200
            frame = "closed" if 2700 <= ms < 2950 else "open"            # blinks every ~3 s
            pm = self.pix("owl", frame)
            bob = int(4 * abs(((age * 1.2) % 2) - 1))                    # gentle bobbing
            x, y = w - pm.width() - 48, h - pm.height() - 12 - bob
            p.drawPixmap(x, y, pm)
            self.bubble(p, "blink!", x + pm.width() // 2, y - 6)
        p.end()

    @staticmethod
    def bubble(p, text, cx, bottom):
        f = QtGui.QFont("Menlo" if sys.platform == "darwin" else "Consolas", 15, QtGui.QFont.Bold)
        p.setFont(f)
        fm = QtGui.QFontMetrics(f)
        tw, th = fm.horizontalAdvance(text) + 24, fm.height() + 12
        rect = QtCore.QRect(cx - tw // 2, bottom - th, tw, th)
        p.setPen(QtGui.QPen(QtGui.QColor(50, 36, 60), 3))
        p.setBrush(QtGui.QColor(255, 250, 240))
        p.drawRoundedRect(rect, 10, 10)
        p.drawText(rect, QtCore.Qt.AlignCenter, text)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--demo", choices=["owl", "snail"], help="loop an animal without the plugin")
    a = ap.parse_args()
    app = QtWidgets.QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    ov = Overlay(demo=a.demo)  # noqa: F841
    print("Overlay running. Ctrl+C to quit." + (f" (demo: {a.demo})" if a.demo else " Waiting for the plugin."))
    import signal
    signal.signal(signal.SIGINT, lambda *_: app.quit())
    t = QtCore.QTimer(); t.start(300); t.timeout.connect(lambda: None)   # lets Ctrl+C through
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
