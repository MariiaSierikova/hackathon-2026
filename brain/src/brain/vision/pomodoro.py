"""Pomodoro logic driven by "is the person at the computer and looking at the screen". Pure logic: no camera, no network.

Feed it one observation every tick with update(now, present, focused). It returns the state and tells you what to send.

 - WORK      the work clock runs only while the person is present AND (looking at the screen OR looked away < away_pause_s).
 - PAUSED    clock stopped: no face for `absent_pause_s`, or eyes off-screen for `lookaway_pause_s`. Continues by itself.
 - DUE       work_s of real work done -> remind (the caller keeps sending break_due; the plugin policy limits the buzzing).
 - After `reset_s` away (a real break) the clock restarts from 0; if that happens while DUE, the break counts as taken.
"""
from dataclasses import dataclass

WORK, PAUSED, DUE = "work", "paused", "due"


@dataclass
class PomodoroConfig:
    work_s: float = 25 * 60        # work time before a break is due
    absent_pause_s: float = 30.0   # no face this long -> pause
    lookaway_pause_s: float = 60.0  # eyes off-screen this long -> pause
    reset_s: float = 5 * 60        # away this long -> counts as a break, clock restarts


class PomodoroTracker:
    def __init__(self, cfg=None):
        self.cfg = cfg or PomodoroConfig()
        self.state = WORK
        self.worked = 0.0            # seconds of real work since the last break
        self._last = None
        self._absent = 0.0           # seconds without a face (current stretch)
        self._unfocused = 0.0        # seconds with a face but eyes off-screen (current stretch)
        self.break_taken = False     # True for one update() after a due break was actually taken

    def update(self, now, present, focused):
        """present: a face is seen. focused: eyes on the screen (only meaningful if present). Returns the state."""
        dt = 0.0 if self._last is None else max(0.0, min(now - self._last, 5.0))   # cap: a stalled loop must not jump the clock
        self._last = now
        self.break_taken = False

        if not present:
            self._absent += dt
            self._unfocused = 0.0
        else:
            self._absent = 0.0
            self._unfocused = 0.0 if focused else self._unfocused + dt

        away = self._absent if not present else 0.0
        # a real break: away long enough -> restart
        if away >= self.cfg.reset_s:
            self.break_taken = self.state == DUE
            self.worked = 0.0
            self.state = PAUSED if present is False else WORK
            return self.state

        paused = (not present and self._absent >= self.cfg.absent_pause_s) or \
                 (present and self._unfocused >= self.cfg.lookaway_pause_s)

        if self.state == DUE:
            return self.state                      # stays due until a real break resets it
        if paused:
            self.state = PAUSED
            return self.state
        self.state = WORK
        # the clock runs for the grace period too (short glances away / short trips are part of work)
        self.worked += dt
        if self.worked >= self.cfg.work_s:
            self.state = DUE
        return self.state

    def remaining_s(self):
        return max(0.0, self.cfg.work_s - self.worked)
