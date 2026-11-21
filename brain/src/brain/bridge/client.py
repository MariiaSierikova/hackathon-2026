"""Send triggers from the camera/mic code to the WorkHealthCoach plugin (same computer, localhost).

    from brain.bridge.client import CoachClient
    coach = CoachClient()
    coach.trigger("blink_low", severity=0.7)   # you have not blinked for a while
    coach.blink_detected()                     # ...call this on EVERY detected blink
    coach.trigger("pace_too_fast", 0.8)
    coach.wpm(172)                             # optional: live words-per-minute for the Keypad

Design rules (so a per-frame loop can call it safely):
- never blocks: messages go to a background thread
- never raises: if the plugin is not running it just returns/ignores
- repeats of the same trigger are throttled (default: at most one every 2 s), so you may call it every frame
"""
import json
import queue
import threading
import time
import urllib.request

# Trigger names the plugin accepts (GET /triggers returns the same list).
PACE_TOO_FAST = "pace_too_fast"
FILLER_BURST = "filler_burst"
EYE_CONTACT_LOST = "eye_contact_lost"
BLINK_LOW = "blink_low"
LOOKAWAY_DONE = "lookaway_done"
PERSON_BEHIND = "person_behind"
BLINK_DETECTED = "blink_detected"   # control: you blinked -> ends the blink reminder
PACE_OK = "pace_ok"                 # control: pace is fine again -> ends the pace reminder


class CoachClient:
    def __init__(self, base="http://127.0.0.1:8787", min_repeat_s=2.0, timeout_s=1.0):
        self.base, self.min_repeat_s, self.timeout_s = base, min_repeat_s, timeout_s
        self._last = {}
        self._q = queue.Queue(maxsize=100)
        self.last_error = None
        threading.Thread(target=self._worker, daemon=True).start()

    # ---- public API ----
    def trigger(self, name, severity=1.0, force=False):
        """Queue a trigger. severity 0.0-1.0 (how clear/strong the problem is). Returns False if throttled."""
        now = time.monotonic()
        if not force and now - self._last.get(name, 0.0) < self.min_repeat_s:
            return False
        self._last[name] = now
        body = {"trigger": name, "severity": max(0.0, min(1.0, float(severity)))}
        if force:
            body["force"] = True            # development only: skips the notification policy
        return self._put("/trigger", body)

    def blink_detected(self):
        return self.trigger(BLINK_DETECTED, 1.0, force=False)

    def pace_ok(self):
        return self.trigger(PACE_OK, 1.0)

    def wpm(self, words_per_minute):
        return self._put("/metrics", {"wpm": float(words_per_minute)})

    def health(self):
        """True if the plugin answers (blocking, <= timeout)."""
        try:
            with urllib.request.urlopen(self.base + "/health", timeout=self.timeout_s) as r:
                return json.loads(r.read()).get("ok") is True
        except Exception as e:
            self.last_error = e
            return False

    # ---- internals ----
    def _put(self, path, body):
        try:
            self._q.put_nowait((path, body))
            return True
        except queue.Full:
            return False

    def _worker(self):
        while True:
            path, body = self._q.get()
            try:
                req = urllib.request.Request(self.base + path, data=json.dumps(body).encode(), method="POST",
                                             headers={"Content-Type": "application/json"})
                urllib.request.urlopen(req, timeout=self.timeout_s).read()
                self.last_error = None
            except Exception as e:   # plugin not running, etc.: remember, never raise
                self.last_error = e
