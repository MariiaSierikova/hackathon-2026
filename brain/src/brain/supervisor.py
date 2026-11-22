"""Starts and stops the camera / microphone monitors so they FOLLOW the plugin's switches (Keypad keys or settings page).

Run it once (`app.py --auto`, see docs/AUTO.md): when you switch Pomodoro on, the gaze tracking (desk presence, pause / resume) and the on-screen timer
start by themselves; switch it off and they stop. Same idea for the others:

  switch                                  monitor that runs
  Pomodoro on  or  Volume when far on     "gaze"   (Gaze Tracker -> presence, face size; the timer chip shows the plugin's clock)
  Speech speed or Speech volume on        "pace"   (microphone)             only when you gave --mic
  Person behind on                        "behind" (infrared camera)        only when you gave --ir-device
  master switch off                       nothing runs
Eye blinking is NOT managed: the camera's AI overheats after ~10 minutes, so it stays a manual `--device N` start.
"""
import time

DEFAULT_ON = {"speed": True, "volume": True, "blink": True, "distance": True, "behind": True, "pomodoro": False}


def read_switches(coach):
    """The plugin's switches as {'master': bool, 'features': {id: bool}}, or None when the plugin does not answer (then nothing changes)."""
    pom = coach.get_json("/pomodoro", timeout_s=0.8)
    if pom is None:
        return None
    ui = coach.get_json("/ui-state", timeout_s=0.8)
    ui = ui if isinstance(ui, dict) else {}
    feats = {}
    for fid, default in DEFAULT_ON.items():
        node = ((ui.get("features") or {}).get(fid) or {}).get("enabled")
        feats[fid] = node if isinstance(node, bool) else default
    feats["pomodoro"] = bool(pom.get("active"))                 # the plugin's own clock is the truth for Pomodoro (Keypad key or page)
    master = ui.get("master")
    return {"master": master if isinstance(master, bool) else True, "features": feats}


def wanted(sw, available):
    """Names of the monitors that should run for these switches."""
    if not sw["master"]:
        return set()
    f = sw["features"]
    want = set()
    if f["pomodoro"] or f["distance"]:
        want.add("gaze")
    if (f["speed"] or f["volume"]) and "pace" in available:
        want.add("pace")
    if f["behind"] and "behind" in available:
        want.add("behind")
    return want


class Supervisor:
    def __init__(self, read, start, stop, is_alive, available, say=print, retry_s=15.0, clock=time.monotonic):
        self.read, self._start, self._stop, self.is_alive = read, start, stop, is_alive
        self.available, self.say, self.retry_s, self.clock = set(available), say, retry_s, clock
        self._started = set()
        self._died_at = {}

    def step(self):
        sw = self.read()
        if sw is None:                       # plugin not answering (restarting?): change nothing
            return
        want = wanted(sw, self.available)
        now = self.clock()
        for name in sorted(want):
            if self.is_alive(name):
                self._died_at.pop(name, None)
                continue
            died = self._died_at.get(name)
            if name in self._started and died is None:           # it ended by itself (error, camera gone ...)
                self._died_at[name] = now
                self.say(f"the {name} monitor stopped by itself; trying again in {self.retry_s:.0f} s")
                continue
            if died is not None and now - died < self.retry_s:
                continue
            self._died_at.pop(name, None)
            self._started.add(name)
            self.say(f"switches say '{name}' is needed -> starting it")
            self._start(name)
        for name in sorted(self._started - want):
            self.say(f"'{name}' is not needed any more -> stopping it")
            self._stop(name)
            self._started.discard(name)
            self._died_at.pop(name, None)

    def stop_all(self):
        for name in sorted(self._started):
            self._stop(name)
        self._started.clear()

    def loop(self, stop_event, every_s=2.0):
        try:
            self.step()
            while not stop_event.wait(every_s):
                self.step()
        finally:
            self.stop_all()
