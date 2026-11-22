"""Run: cd brain && uv run python tests/test_settings.py"""
import sys
from argparse import Namespace
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "vendor" / "logiinsight"))
from brain.monitors.pace_monitor import thresholds  # noqa: E402
from brain.settings import dbfs_to_page_db, page_db_to_dbfs, wpm_to_syllables  # noqa: E402

NO_FLAGS = Namespace(fast_rate=None, ok_rate=None, quiet_db=None, ok_db=None)


def test_conversions():
    assert abs(wpm_to_syllables(170) - 4.25) < 1e-9
    assert page_db_to_dbfs(50) == -25.0                  # the page default equals the tested -25 dBFS threshold
    assert dbfs_to_page_db(-28) == 47


def test_page_wins_over_builtin_and_flag_wins_over_page():
    fast, ok, quiet, ok_db, where = thresholds(NO_FLAGS, {"fast_wpm": 200, "quiet_db": 55})
    assert abs(fast - 5.0) < 1e-9 and abs(ok - 4.3) < 1e-9 and quiet == -20 and ok_db == -17 and len(where) == 2
    flags = Namespace(fast_rate=6.0, ok_rate=None, quiet_db=-30.0, ok_db=None)
    fast, ok, quiet, ok_db, where = thresholds(flags, {"fast_wpm": 200, "quiet_db": 55})
    assert fast == 6.0 and quiet == -30.0 and where == []


def test_no_page_keeps_the_tested_defaults():
    fast, ok, quiet, ok_db, where = thresholds(NO_FLAGS, {})
    assert (fast, ok, quiet, ok_db, where) == (5.0, 4.3, -25.0, -22.0, [])


if __name__ == "__main__":
    for n, f in list(globals().items()):
        if n.startswith("test_"):
            f()
            print("ok", n)
