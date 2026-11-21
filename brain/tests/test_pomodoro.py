"""Run: cd brain && python3 tests/test_pomodoro.py   (no dependencies)"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from brain.vision.pomodoro import DUE, PAUSED, WORK, PomodoroConfig, PomodoroTracker  # noqa: E402

CFG = PomodoroConfig(work_s=100, absent_pause_s=10, lookaway_pause_s=20, reset_s=60)


def run(tr, seconds, present=True, focused=True, t=[0.0]):
    s = None
    for _ in range(int(seconds)):
        t[0] += 1.0
        s = tr.update(t[0], present, focused)
    return s


def test_runs_to_due():
    tr = PomodoroTracker(CFG)
    assert run(tr, 99) == WORK and run(tr, 2) == DUE


def test_absence_pauses_and_resumes_without_counting():
    tr = PomodoroTracker(CFG)
    run(tr, 50)
    assert run(tr, 30, present=False) == PAUSED
    w = tr.worked
    assert run(tr, 5, present=False) == PAUSED and tr.worked == w     # clock stopped
    assert run(tr, 1) == WORK                                          # back -> continues
    assert 50 < tr.worked < 70                                         # only the 10 s grace counted


def test_lookaway_pauses():
    tr = PomodoroTracker(CFG)
    run(tr, 10)
    assert run(tr, 30, focused=False) == PAUSED
    assert run(tr, 1, focused=True) == WORK


def test_short_glance_keeps_running():
    tr = PomodoroTracker(CFG)
    run(tr, 10)
    assert run(tr, 15, focused=False) == WORK


def test_long_absence_resets_and_counts_as_break_when_due():
    tr = PomodoroTracker(CFG)
    run(tr, 101)
    assert tr.state == DUE
    run(tr, 30, present=False)
    assert tr.state == DUE                                             # short trip: still due
    run(tr, 40, present=False)
    assert tr.worked == 0.0
    assert run(tr, 1) == WORK


def test_break_taken_flag_fires_once():
    tr = PomodoroTracker(CFG)
    run(tr, 101)
    seen = []
    for _ in range(80):
        run(tr, 1, present=False)
        seen.append(tr.break_taken)
    assert seen.count(True) == 1


def test_stalled_loop_does_not_jump_clock():
    tr = PomodoroTracker(CFG)
    tr.update(0.0, True, True)
    tr.update(1000.0, True, True)
    assert tr.worked <= 5.0


if __name__ == "__main__":
    for n, f in list(globals().items()):
        if n.startswith("test_"):
            f()
            print("ok", n)
