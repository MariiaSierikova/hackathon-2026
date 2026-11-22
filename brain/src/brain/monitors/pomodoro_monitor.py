"""Pomodoro presence monitor: tells the plugin whether you are at the computer AND looking at the screen.

Presence and focus come from the Logi Gaze Tracker app (tray app, ws://127.0.0.1:8964, calibrate it once: docs/POMODORO.md).
 - face seen, eyes on the screen                       -> at the desk
 - no face for 30 s / eyes off-screen for 60 s         -> away (a short glance or trip does not count)

DEFAULT (the plugin owns the clock): this program only sends /signal/presence/true|false. The plugin runs the timer (settings page:
focus/break minutes, Keypad timer, break_due / break_over notifications). Switch Pomodoro ON on the settings page
(http://127.0.0.1:8787/) or with the Pomodoro key on the Keypad. The on-screen chip (app.py --pomodoro) shows the plugin's clock.

--own-timer (old standalone mode): this program keeps its own clock and sends break_due every 2 s until you take the break.
Do not use both clocks at once: two clocks mean double reminders.

    cd brain
    uv run python src/brain/monitors/pomodoro_monitor.py --simulate        # NO hardware, 80 s, shortened times
    uv run python src/brain/monitors/pomodoro_monitor.py                   # real: Gaze Tracker must be running + calibrated
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


STATUS = {"state": None, "remaining": 0.0}      # read by the on-screen chip (overlay/pomodoro_chip.py)


def get_status():
    return STATUS["state"], STATUS["remaining"]


class GazeFeed:
    """Background thread: keeps the newest Gaze Tracker snapshot. `connected` is False while the app is not reachable."""

    def __init__(self, url="ws://127.0.0.1:8964", interval_ms=200):
        self.url, self.interval_ms = url, interval_ms
        self.latest, self.latest_at, self.connected, self.error = None, 0.0, False, None
        self.last_ts = None
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
                            # the server re-sends the newest snapshot every interval: only a NEW timestamp_ms is a new camera frame
                            if msg.get("timestamp_ms") != self.last_ts:
                                self.last_ts = msg.get("timestamp_ms")
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
    ap.add_argument("--remind-every", type=float, default=2.0, help="seconds between break_due messages while a break is due (--own-timer only)")
    ap.add_argument("--own-timer", action="store_true", help="old mode: keep the clock here and send break_due yourself (default: the plugin owns the clock)")


def run(a, stop, say=print):
    coach = CoachClient(min_repeat_s=2.0)
    if not coach.health():
        say("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787, messages are only printed.")
    own = getattr(a, "own_timer", False)
    if a.simulate:
        cfg = PomodoroConfig(work_s=40, absent_pause_s=5, lookaway_pause_s=8, reset_s=14)
        feed = FakeGaze()
        say("SIMULATION (times shortened): away 10-22 s and off-screen 35-48 s -> not at the desk (after a 5-8 s grace). Runs 80 s."
            + (" Own timer: 40 s of work, DUE ~52 s, away 58-75 s = break taken." if own else ""))
    else:
        cfg = PomodoroConfig(a.work_min * 60, a.absent_pause, a.lookaway_pause, a.break_min * 60)
        feed = GazeFeed(a.gaze_url)
        say(f"Pomodoro: needs the Gaze Tracker app running + calibrated (ws {a.gaze_url}).")
    if not own:
        # presence only: the plugin owns the clock, so this tracker must never start a work clock or a break of its own
        cfg = PomodoroConfig(work_s=1e12, absent_pause_s=cfg.absent_pause_s, lookaway_pause_s=cfg.lookaway_pause_s, reset_s=1e12)
        say("The plugin owns the Pomodoro clock: sending presence only. Switch Pomodoro ON at http://127.0.0.1:8787/ (or the Pomodoro key).")
    tracker = PomodoroTracker(cfg)
    last_presence, last_presence_sent, last_poll, hinted = None, 0.0, 0.0, False
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
                STATUS["state"] = None               # chip hides: no data, no clock
                time.sleep(0.2)
                continue
            warned = False
            snap = feed.latest if now - feed.latest_at < 3.0 else None      # stale data = nobody seen
            present, focused = observe(snap)
            state = tracker.update(now, present, focused)
            if own:
                STATUS["state"], STATUS["remaining"] = state, tracker.remaining_s()
            else:
                at_desk = state != PAUSED
                if at_desk != last_presence or now - last_presence_sent >= 10:      # on change, and a heartbeat (plugin restarts)
                    coach.signal("presence", at_desk)
                    last_presence, last_presence_sent = at_desk, now
                if now - last_poll >= 1.0:                                           # the chip shows the PLUGIN's clock
                    last_poll = now
                    p = coach.get_json("/pomodoro")
                    if p is None or not p.get("active"):
                        STATUS["state"] = None
                        if p is not None and not hinted:
                            say("Pomodoro is switched OFF in the plugin (no clock, no chip): turn it on at http://127.0.0.1:8787/ or with the Pomodoro key.")
                            hinted = True
                    else:
                        hinted = False
                        STATUS["remaining"] = p.get("left", 0)
                        STATUS["state"] = "break" if p.get("phase") != "work" else ("work" if p.get("counting") else "paused")
            if state != last_state:
                if own:
                    say(f"[{now - t0:5.0f}s] {state.upper()}  (worked {tracker.worked:.0f}s, "
                        f"{'face' if present else 'no face'}, {'on screen' if focused else 'off screen'})")
                else:
                    say(f"[{now - t0:5.0f}s] {'AT THE DESK' if state != PAUSED else 'AWAY (not here / not looking)'} "
                        f"({'face' if present else 'no face'}, {'on screen' if focused else 'off screen'}) -> plugin")
                last_state = state
            if own and state == DUE and now - last_remind >= a.remind_every:
                last_remind = now
                coach.trigger(BREAK_DUE, 0.7, force=a.force)
            if own and tracker.break_taken:
                say(f"[{now - t0:5.0f}s] break taken -> clock restarted")
                coach.trigger(LOOKAWAY_DONE, 1.0, force=a.force)
            if own and now - last_status >= 10 and state != DUE:
                last_status = now
                say(f"[{now - t0:5.0f}s] {state}: {tracker.remaining_s() / 60:.1f} min of work left"
                    + (" (paused: not looking / not here)" if state == PAUSED else ""))
            time.sleep(0.2)
    finally:
        STATUS["state"] = None
        feed.stop()
        if not own:
            coach.signal("presence", True)      # do not leave the plugin clock frozen "away" when this program stops
            time.sleep(0.3)
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
