"""Run: cd brain && uv run python tests/test_supervisor.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from brain.supervisor import DEFAULT_ON, Supervisor, read_switches, wanted  # noqa: E402


def sw(master=True, **features):
    f = dict(DEFAULT_ON)
    f.update(features)
    return {"master": master, "features": f}


class Rig:
    def __init__(self, available=("gaze", "pace", "behind")):
        self.state, self.alive, self.log, self.now = sw(), set(), [], 0.0
        self.sup = Supervisor(lambda: self.state, self.start, self.stop, lambda n: n in self.alive, available, say=lambda m: None,
                              retry_s=15.0, clock=lambda: self.now)

    def start(self, n):
        self.alive.add(n)
        self.log.append("start " + n)

    def stop(self, n):
        self.alive.discard(n)
        self.log.append("stop " + n)


def test_wanted():
    A = {"gaze", "pace", "behind"}
    assert wanted(sw(distance=False, speed=False, volume=False, behind=False), A) == set()
    assert wanted(sw(distance=False, speed=False, volume=False, behind=False, pomodoro=True), A) == {"gaze"}
    assert wanted(sw(), A) == {"gaze", "pace", "behind"}                     # defaults: everything but Pomodoro
    assert wanted(sw(), {"gaze"}) == {"gaze"}                                # no --mic / --ir-device given: only the gaze monitor can run
    assert wanted(sw(master=False, pomodoro=True), A) == set()               # master off: nothing runs


def test_pomodoro_on_starts_gaze_and_off_stops_it():
    r = Rig()
    r.state = sw(distance=False, speed=False, volume=False, behind=False)
    r.sup.step()
    assert r.log == []
    r.state = sw(distance=False, speed=False, volume=False, behind=False, pomodoro=True)     # the Keypad key is pressed
    r.sup.step()
    r.sup.step()
    assert r.log == ["start gaze"]                                                            # started once, not on every poll
    r.state = sw(distance=False, speed=False, volume=False, behind=False)
    r.sup.step()
    assert r.log == ["start gaze", "stop gaze"] and not r.alive


def test_plugin_not_answering_changes_nothing():
    r = Rig()
    r.state = sw(pomodoro=True)
    r.sup.step()
    before = list(r.log)
    r.state = None
    r.sup.step()
    assert r.log == before and r.alive == {"gaze", "pace", "behind"}


def test_a_monitor_that_dies_is_restarted_after_the_wait_not_in_a_loop():
    r = Rig(available=("gaze",))
    r.state = sw(distance=False, speed=False, volume=False, behind=False, pomodoro=True)
    r.sup.step()
    r.alive.clear()                                                           # it crashed
    r.now = 1.0
    r.sup.step()
    assert r.log == ["start gaze"]                                            # noticed, waiting
    r.now = 10.0
    r.sup.step()
    assert r.log == ["start gaze"]
    r.now = 17.0
    r.sup.step()
    assert r.log == ["start gaze", "start gaze"]


def test_master_off_stops_everything_and_stop_all():
    r = Rig()
    r.sup.step()
    assert r.alive == {"gaze", "pace", "behind"}
    r.state = sw(master=False)
    r.sup.step()
    assert not r.alive
    r.state = sw()
    r.sup.step()
    r.sup.stop_all()
    assert not r.alive


class FakeCoach:
    def __init__(self, answers):
        self.answers = answers

    def get_json(self, path, timeout_s=0.4):
        return self.answers.get(path)


def test_read_switches():
    assert read_switches(FakeCoach({})) is None
    s = read_switches(FakeCoach({"/pomodoro": {"active": True}, "/ui-state": {"master": True, "features": {"speed": {"enabled": False}, "pomodoro": {"enabled": False}}}}))
    assert s["features"]["pomodoro"] is True and s["features"]["speed"] is False and s["features"]["volume"] is True and s["master"] is True
    s = read_switches(FakeCoach({"/pomodoro": {"active": False}}))                    # page never opened: no ui-state
    assert s["features"]["pomodoro"] is False and s["features"]["distance"] is True


if __name__ == "__main__":
    for n, f in list(globals().items()):
        if n.startswith("test_"):
            f()
            print("ok", n)
