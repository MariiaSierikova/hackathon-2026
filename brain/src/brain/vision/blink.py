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
    """Adaptive threshold: the eye is 'closed' when EAR < ratio * the recent eyes-open level.

    A blink only counts when the eye stayed closed for at least `min_samples` consecutive results and the dip was
    deep (below depth_ratio * open level). That filters single noisy frames, which would otherwise count as blinks
    and keep resetting the "no blink for N seconds" timer. Closures up to max_ms (2.5 s) count: a slow blink or a
    moment with the eyes shut rests the eyes just as well (checked on a real Windows log, 2026-11-21).
    """

    def __init__(self, ratio=0.72, min_ms=40, max_ms=2500, refractory_ms=180, min_samples=2, depth_ratio=0.62):
        self.ratio, self.min_ms, self.max_ms, self.refractory_ms = ratio, min_ms, max_ms, refractory_ms
        self.min_samples, self.depth_ratio = min_samples, depth_ratio
        self.history = collections.deque(maxlen=150)   # ~5 s of samples
        self.closed_since = None
        self.closed_samples = 0
        self.min_e = 1.0
        self.last_blink = 0.0
        self.closed = False                             # state after the last update (for logging)

    def baseline(self):
        if len(self.history) < 20:
            return None
        s = sorted(self.history)
        return s[int(len(s) * 0.85)]                   # high percentile = eyes-open level

    def update(self, e, now):
        """Feed one EAR sample. Returns the blink duration in ms when a blink just finished, else None."""
        base = self.baseline()
        closed = base is not None and e < base * self.ratio
        self.closed = closed
        if not closed:
            self.history.append(e)                      # learn only from open-eye samples
        if closed:
            if self.closed_since is None:
                self.closed_since, self.closed_samples, self.min_e = now, 0, e
            self.closed_samples += 1
            self.min_e = min(self.min_e, e)
        elif self.closed_since is not None:
            dur = (now - self.closed_since) * 1000
            deep = base is not None and self.min_e < base * self.depth_ratio
            ok = (self.min_ms <= dur <= self.max_ms and self.closed_samples >= self.min_samples and deep
                  and (now - self.last_blink) * 1000 >= self.refractory_ms)
            self.closed_since = None
            if ok:
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
