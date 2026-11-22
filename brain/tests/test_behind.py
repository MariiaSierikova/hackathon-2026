"""Run: cd brain && uv run python tests/test_behind.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "vendor" / "logiinsight"))
from brain.monitors.pomodoro_monitor import face_width  # noqa: E402
from brain.vision.faces import Face, IdTracker, split_user_and_others, to_faces  # noqa: E402


def test_geometry():
    f = to_faces([(100, 50, 80, 80)], 512)[0]
    assert abs(f.x - 140 / 512) < 1e-9 and abs(f.size - 80 / 512) < 1e-9


def test_user_is_the_biggest_face():
    user, others = split_user_and_others([Face(0.8, 0.07), Face(0.5, 0.30), Face(0.2, 0.05)])
    assert user.size == 0.30 and [o.size for o in others] == [0.07, 0.05]
    assert split_user_and_others([]) == (None, [])
    assert split_user_and_others([Face(0.5, 0.3)])[1] == []


def test_ids_are_stable_and_new_people_get_new_ids():
    t = IdTracker()
    a = t.assign([Face(0.80, 0.06)], 0.0)
    b = t.assign([Face(0.74, 0.07)], 0.5)                       # the same person moved a little
    assert a[0][0] == b[0][0]
    c = t.assign([Face(0.74, 0.07), Face(0.20, 0.06)], 1.0)     # a second person appears
    assert len({i for i, _ in c}) == 2 and a[0][0] in {i for i, _ in c}
    ids_second = [i for i, f in c if f.x < 0.3][0]
    for now in (2.0, 3.0, 4.0, 5.0):                            # the first one left, the second stays and is seen all the time
        d = t.assign([Face(0.20, 0.06)], now)
    assert d[0][0] == ids_second
    e = t.assign([Face(0.80, 0.06)], 9.0)                       # someone at the old place long after (ttl) is a NEW visit
    assert e[0][0] not in {a[0][0], ids_second}


def test_two_people_crossing_keep_their_own_ids():
    t = IdTracker()
    t.assign([Face(0.30, 0.06), Face(0.70, 0.06)], 0.0)
    out = dict(t.assign([Face(0.34, 0.06), Face(0.66, 0.06)], 0.5))
    assert len(out) == 2 and out[min(out)].x < out[max(out)].x


def test_face_width_from_the_gaze_tracker_snapshot():
    assert face_width({"face": {"bounding_box_norm": {"x": .4, "y": .3, "width": 0.17, "height": .3}}}) == 0.17
    assert face_width({"face": None}) is None and face_width(None) is None
    assert face_width({"face": {"bounding_box_norm": {"width": 0}}}) is None


if __name__ == "__main__":
    for n, f in list(globals().items()):
        if n.startswith("test_"):
            f()
            print("ok", n)
