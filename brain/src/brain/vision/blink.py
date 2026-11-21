"""Blink logic shared by the probe and the monitor (no camera code here, so it can be tested without a camera)."""
import collections

# MediaPipe 468-point face mesh indices: [outer corner, upper1, upper2, inner corner, lower2, lower1]
EYES = {
    "right": (33, 160, 158, 133, 153, 144),
    "left": (362, 385, 387, 263, 373, 380),
}


def ear(lm, idx):
    """Eye aspect ratio from 6 landmarks (x, y in stream pixels). ~0.3 open, ~0.1 closed."""
    p = [lm[i] for i in idx]
    d = lambda a, b: ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5  # noqa: E731
    horiz = d(p[0], p[3])
    return (d(p[1], p[5]) + d(p[2], p[4])) / (2.0 * horiz) if horiz > 1e-6 else None


def mean_ear(landmarks):
    """Average EAR of both eyes, or None when the mesh is missing/incomplete."""
    if not landmarks or len(landmarks) < 400:
        return None
    vals = [v for v in (ear(landmarks, EYES["right"]), ear(landmarks, EYES["left"])) if v]
    return sum(vals) / len(vals) if vals else None


class BlinkDetector:
    """Adaptive threshold: eye is 'closed' when EAR < ratio * recent open level."""

    def __init__(self, ratio=0.72, min_ms=40, max_ms=600, refractory_ms=180):
        self.ratio, self.min_ms, self.max_ms, self.refractory_ms = ratio, min_ms, max_ms, refractory_ms
        self.history = collections.deque(maxlen=150)   # ~5 s of samples
        self.closed_since = None
        self.last_blink = 0.0

    def baseline(self):
        if len(self.history) < 20:
            return None
        s = sorted(self.history)
        return s[int(len(s) * 0.85)]                   # high percentile = eyes-open level

    def update(self, e, now):
        """Feed one EAR sample. Returns the blink duration in ms when a blink just finished, else None."""
        base = self.baseline()
        closed = base is not None and e < base * self.ratio
        if not closed:
            self.history.append(e)                      # learn only from open-eye samples
        if closed and self.closed_since is None:
            self.closed_since = now
        elif not closed and self.closed_since is not None:
            dur = (now - self.closed_since) * 1000
            self.closed_since = None
            if self.min_ms <= dur <= self.max_ms and (now - self.last_blink) * 1000 >= self.refractory_ms:
                self.last_blink = now
                return dur
        return None


class NoBlinkWatcher:
    """Turns 'time since the last blink' into a severity for the blink_low trigger.

    No face in view = the user is away: the clock restarts, nothing is sent.
    severity: 0.5 when the gap reaches `after_s`, +0.5 spread over `ramp_s` more seconds, max 1.0.
    """

    def __init__(self, after_s=8.0, ramp_s=12.0, face_lost_s=3.0):
        self.after_s, self.ramp_s, self.face_lost_s = after_s, ramp_s, face_lost_s
        self.last_blink = None
        self.last_face = None

    def blink(self, now):
        self.last_blink = now

    def face_seen(self, now):
        if self.last_blink is None:
            self.last_blink = now
        self.last_face = now

    def severity(self, now):
        """Severity 0.5-1.0 when a reminder is due, else None."""
        if self.last_blink is None or self.last_face is None:
            return None
        if now - self.last_face > self.face_lost_s:      # nobody there
            self.last_blink = now
            return None
        gap = now - self.last_blink
        if gap < self.after_s:
            return None
        return min(1.0, 0.5 + 0.5 * (gap - self.after_s) / self.ramp_s)
