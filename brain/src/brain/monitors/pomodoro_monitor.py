"""Pomodoro monitor: 25 min of REAL work (you are at the computer and look at the screen), then break_due.

Presence and focus come from the Logi Gaze Tracker app (tray app, ws://127.0.0.1:8964, calibrate it once: docs/POMODORO.md).
 - face seen, eyes on the screen -> the work clock runs
 - no face for 30 s / eyes off-screen for 60 s -> the clock PAUSES (continues by itself when you are back)
 - away for 5 min -> counts as a break, clock restarts
 - 25 min of work done -> break_due is sent every 2 s until you really take the break (lookaway_done is sent then)

    cd brain
    uv run python src/brain/monitors/pomodoro_monitor.py --simulate        # NO hardware, 70 s, shortened times
    uv run python src/brain/monitors/pomodoro_monitor.py --work-min 25     # real: Gaze Tracker must be running + calibrated
"""
import argparse
import json
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve()
sys.path.insert(0, str(HERE.parents[2]))                       # brain/src

from brain.bridge.client import BREAK_DUE, LOOKAWAY_DONE, CoachClient  # noqa: E402
from brain.vision.pomodoro import DUE, PAUSED, PomodoroConfig, PomodoroTracker  # noqa: E402


class GazeFeed:
    """Background thread: keeps the newest Gaze Tracker snapshot. `connected` is False while the app is not reachable."""

    def __init__(self, url="ws://127.0.0.1:8964", interval_ms=200):
        self.url, self.interval_ms = url, interval_ms
        self.latest, self.latest_at, self.connected, self.error = None, 0.0, False, None
        self._stop = threading.Event()
        threading.Thread(target=self._run, daemon=True).start()

    def stop(self):
        self._stop.set()

    def _run(self):
        from websockets.sync.client import connect
        while not self._stop.is_set():
            try:
                with connect(self.url, open_timeout=3) as ws:
                    self.connected, self.error = True, None
                    ws.send(json.dumps({"type": "start_stream", "interval_ms": self.interval_ms}))
                    while not self._stop.is_set():
                        try:
                            msg = json.loads(ws.recv(timeout=2))
                        except TimeoutError:
                            continue
                        if msg.get("type") == "snapshot":
                            self.latest, self.latest_at = msg, time.monotonic()
            except Exception as e:                 # app not started yet / closed: retry
                self.error = e
            self.connected = False
            self._stop.wait(2.0)


def observe(snap, margin=0.05):
    """Snapshot -> (present, focused). gaze is null when there is no face OR no calibration, so presence also uses `face`."""
    if not snap:
        return False, False
    gaze, face = snap.get("gaze"), snap.get("face")
    present = bool(gaze) or bool(face and face.get("bounding_box_norm"))
    focused = False
    if gaze:
        scr = snap.get("screen") or {}
        w, h = scr.get("width_px") or 0, scr.get("height_px") or 0
        if w and h:
            focused = (-margin * w <= gaze["x_px"] <= (1 + margin) * w) and (-margin * h <= gaze["y_px"] <= (1 + margin) * h)
    return present, focused


class FakeGaze:
    """--simulate: at the desk, leaves 10-22 s, back, looks off-screen 35-48 s, back (break due ~52 s), leaves 58-75 s."""

    def __init__(self):
        self.t0 = time.monotonic()
        self.connected = True

    @property
    def latest(self):
        t = time.monotonic() - self.t0
        scr = {"width_px": 1920, "height_px": 1080}
        if 10 <= t < 22 or 58 <= t < 75:
            return {"screen": scr, "gaze": None, "face": None}
        x = 5000 if 35 <= t < 48 else 900
        return {"screen": scr, "gaze": {"x_px": x, "y_px": 500}, "face": {"bounding_box_norm": {"x": .3}}}

    @property
    def latest_at(self):
        return time.monotonic()

    def stop(self): pass


def add_args(ap):
    ap.add_argument("--work-min", type=float, default=25.0, help="minutes of real work until a break is due")
    ap.add_argument("--absent-pause", type=float, default=30.0, help="seconds without a face before the clock pauses")
    ap.add_argument("--lookaway-pause", type=float, default=60.0, help="seconds looking off-screen before the clock pauses")
    ap.add_argument("--break-min", type=float, default=5.0, help="minutes away that count as a taken break")
    ap.add_argument("--gaze-url", default="ws://127.0.0.1:8964")
    ap.add_argument("--remind-every", type=float, default=2.0, help="seconds between break_due messages while a break is due")


def run(a, stop, say=print):
    coach = CoachClient(min_repeat_s=2.0)
    if not coach.health():
        say("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787, messages are only printed.")
    if a.simulate:
        cfg = PomodoroConfig(work_s=40, absent_pause_s=5, lookaway_pause_s=8, reset_s=14)
        feed = FakeGaze()
        say("SIMULATION (times shortened, 40 s of work): away 10-22 s and off-screen 35-48 s -> PAUSED; DUE ~52 s; away 58-75 s -> break taken. Runs 80 s.")
    else:
        cfg = PomodoroConfig(a.work_min * 60, a.absent_pause, a.lookaway_pause, a.break_min * 60)
        feed = GazeFeed(a.gaze_url)
        say(f"Pomodoro: {a.work_min:.0f} min of work. Needs the Gaze Tracker app running + calibrated (ws {a.gaze_url}).")
    tracker = PomodoroTracker(cfg)
    t0 = time.monotonic()
    last_state, last_remind, last_status, warned = None, 0.0, t0, False
    try:
        while not stop.is_set():
            now = time.monotonic()
            if a.simulate and now - t0 > 80:
                break
            if not feed.connected:
                if not warned and now - t0 > 3:
                    say("Gaze Tracker not reachable yet (start the app, tray icon). The clock does not run until it is.")
                    warned = True
                tracker._last = None                 # a gap in data must not count as time
                time.sleep(0.2)
                continue
            warned = False
            snap = feed.latest if now - feed.latest_at < 3.0 else None      # stale data = nobody seen
            present, focused = observe(snap)
            state = tracker.update(now, present, focused)
            if state != last_state:
                say(f"[{now - t0:5.0f}s] {state.upper()}  (worked {tracker.worked:.0f}s, "
                    f"{'face' if present else 'no face'}, {'on screen' if focused else 'off screen'})")
                last_state = state
            if state == DUE and now - last_remind >= a.remind_every:
                last_remind = now
                coach.trigger(BREAK_DUE, 0.7, force=a.force)
            if tracker.break_taken:
                say(f"[{now - t0:5.0f}s] break taken -> clock restarted")
                coach.trigger(LOOKAWAY_DONE, 1.0, force=a.force)
            if now - last_status >= 10 and state != DUE:
                last_status = now
                say(f"[{now - t0:5.0f}s] {state}: {tracker.remaining_s() / 60:.1f} min of work left"
                    + (" (paused: not looking / not here)" if state == PAUSED else ""))
            time.sleep(0.2)
    finally:
        feed.stop()
        say("Pomodoro monitor stopped.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_args(ap)
    ap.add_argument("--force", action="store_true", help="demo: the plugin skips its notification rules")
    ap.add_argument("--simulate", action="store_true", help="NO hardware: scripted fake person")
    a = ap.parse_args()
    stop = threading.Event()
    try:
        run(a, stop)
    except KeyboardInterrupt:
        stop.set()


if __name__ == "__main__":
    main()
