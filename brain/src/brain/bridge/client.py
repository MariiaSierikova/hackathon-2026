"""Send triggers from the camera/mic code to the WorkHealthCoach plugin (same computer, localhost).

    from brain.bridge.client import CoachClient
    coach = CoachClient()
    coach.trigger("blink_low", severity=0.7)   # you have not blinked for a while
    coach.blink_detected()                     # ...call this on EVERY detected blink
    coach.signal("pace_too_fast", True)        # plain True/False: speech too fast / fine again
    coach.wpm(172)                             # optional: live words-per-minute for the Keypad

Design rules (so a per-frame loop can call it safely):
- never blocks: messages go to a background thread
- never raises: if the plugin is not running it just returns/ignores (see .last_error)
- repeats of the same trigger are ALWAYS throttled (default: at most one every 2 s), so you may call it every frame
- if the plugin is slow, old messages are dropped so a fresh "blink_detected" never waits behind a pile of old ones
"""
import json
import os
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
SPEAK_UP = "speak_up"             # too quiet -> duck
SPEAK_OK = "speak_ok"             # control: voice is loud enough again -> ends the duck
BREAK_DUE = "break_due"           # Pomodoro: time for a break
BLINK_DETECTED = "blink_detected"   # control: you blinked -> ends the blink reminder
PACE_OK = "pace_ok"                 # control: pace is fine again -> ends the pace reminder


class CoachClient:
    def __init__(self, base=None, min_repeat_s=2.0, timeout_s=1.0):
        # WORKHEALTH_PORT is the same switch the plugin has (tests run a second server); default 8787
        base = base or f"http://127.0.0.1:{os.environ.get('WORKHEALTH_PORT', '8787')}"
        self.base, self.min_repeat_s, self.timeout_s = base, min_repeat_s, timeout_s
        self._last = {}
        self._q = queue.Queue(maxsize=20)
        self.last_error = None
        self.sent = 0           # messages the plugin accepted
        self.failed = 0         # messages that could not be delivered
        threading.Thread(target=self._worker, daemon=True).start()

    # ---- public API ----
    def trigger(self, name, severity=1.0, force=False):
        """Queue a trigger. severity 0.0-1.0 (how clear/strong the problem is).

        force=True is for demos only: the plugin skips its notification rules (cooldown, snooze...).
        It does NOT turn off the repeat limit above. Returns False if throttled.
        """
        now = time.monotonic()
        if now - self._last.get(name, 0.0) < self.min_repeat_s:
            return False
        self._last[name] = now
        body = {"trigger": name, "severity": max(0.0, min(1.0, float(severity)))}
        if force:
            body["force"] = True
        return self._put("/trigger", body)

    def signal(self, name, on, force=False):
        """Plain True/False: signal('pace_too_fast', True) = problem, False = problem over. Throttled like trigger()."""
        key = f"signal:{name}:{bool(on)}"
        now = time.monotonic()
        if now - self._last.get(key, 0.0) < self.min_repeat_s:
            return False
        self._last[key] = now
        return self._put(f"/signal/{name}/{'true' if on else 'false'}" + ("?force=1" if force else ""), None)

    def blink_detected(self):
        return self.trigger(BLINK_DETECTED, 1.0)

    def pace_ok(self):
        return self.trigger(PACE_OK, 1.0)

    def wpm(self, words_per_minute):
        return self._put("/metrics", {"wpm": float(words_per_minute)})

    def behind(self, samples):
        """Samples of OTHER faces for the plugin's person-behind logic: [{"id":2,"x":0.62,"size":0.07}, ...]. Send a few times a second while
        someone is there; stop sending when nobody is."""
        if not samples:
            return False
        return self._put("/behind", {"samples": list(samples)})

    def distance(self, face=None, ratio=None):
        """Face width (needs the plugin's baseline) or a ready ratio (face size vs normal) for the volume-when-far feature. Keep sending: stale after 5 s."""
        body = {"face": float(face)} if face is not None else {"ratio": float(ratio)}
        return self._put("/distance", body)

    def get_json(self, path, timeout_s=0.4):
        """GET a JSON answer from the plugin (blocking, short timeout). None if the plugin does not answer."""
        try:
            with urllib.request.urlopen(self.base + path, timeout=timeout_s) as r:
                return json.loads(r.read())
        except Exception as e:
            self.last_error = e
            return None

    def post_json(self, path, timeout_s=1.0, body=None):
        """Blocking POST (empty body, or `body` as JSON). Returns the JSON answer, or None if the plugin does not answer or says no."""
        try:
            data = json.dumps(body).encode() if body is not None else b""
            req = urllib.request.Request(self.base + path, data=data, method="POST", headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=timeout_s) as r:
                return json.loads(r.read() or b"{}")
        except Exception as e:
            self.last_error = e
            return None

    def health(self):
        """True if the plugin answers (blocking, <= timeout). The answer also names the plugin build."""
        try:
            with urllib.request.urlopen(self.base + "/health", timeout=self.timeout_s) as r:
                data = json.loads(r.read())
                self.build = data.get("build")
                return data.get("ok") is True
        except Exception as e:
            self.last_error = e
            return False

    # ---- internals ----
    def _put(self, path, body):
        item = (path, body)
        try:
            self._q.put_nowait(item)
            return True
        except queue.Full:
            try:                       # drop the oldest message, keep the freshest
                self._q.get_nowait()
                self._q.put_nowait(item)
                return True
            except Exception:
                return False

    def _worker(self):
        while True:
            path, body = self._q.get()
            try:
                data = json.dumps(body).encode() if body is not None else b""
                req = urllib.request.Request(self.base + path, data=data, method="POST",
                                             headers={"Content-Type": "application/json"})
                urllib.request.urlopen(req, timeout=self.timeout_s).read()
                self.last_error = None
                self.sent += 1
            except Exception as e:   # plugin not running, etc.: remember, never raise
                self.last_error = e
                self.failed += 1
