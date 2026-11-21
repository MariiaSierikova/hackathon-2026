"""Run: cd brain && python3 tests/test_loudness.py   (needs numpy + scipy: use `uv run python tests/test_loudness.py`)"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from brain.audio.loudness import LoudnessState, estimate_loudness  # noqa: E402

FS = 48000
rng = np.random.default_rng(1)


def speech(amplitude, seconds=5.0, pause_every=None):
    """Noise-like 'speech' in the speech band (stand-in for a voice) with a given RMS amplitude."""
    t = np.arange(int(seconds * FS)) / FS
    x = sum(np.sin(2 * np.pi * f * t + rng.uniform(0, 6)) for f in (350, 700, 1100, 1700, 2400))
    x = x / np.sqrt(np.mean(x ** 2)) * amplitude
    if pause_every:
        x[(t % pause_every) > pause_every / 2] = 0.0
    return x


def db(a):
    return 20 * np.log10(a)


def test_level_is_measured():
    assert abs(estimate_loudness(speech(0.1)) - db(0.1)) < 1.0


def test_silence_gives_no_verdict():
    assert estimate_loudness(np.zeros(5 * FS)) is None


def test_pauses_do_not_make_speech_look_quiet():
    # half of the window is silence: the teammate's whole-window RMS would read ~3 dB lower; ours must not
    assert abs(estimate_loudness(speech(0.1, pause_every=1.0)) - db(0.1)) < 1.0


def test_quiet_flagged_and_loud_fine():
    st = LoudnessState()
    assert st.update(estimate_loudness(speech(0.02))) is True      # about -34 dBFS
    assert st.update(estimate_loudness(speech(0.2))) is False      # about -14 dBFS


def test_hysteresis():
    st = LoudnessState()
    assert st.update(-30) and st.update(-23.5) and not st.update(-21.9)
    assert not st.update(-24)                                      # between thresholds: stays fine


def test_severity():
    st = LoudnessState()
    assert st.severity(-25) == 0.5 and st.severity(-40) == 1.0


if __name__ == "__main__":
    for n, f in list(globals().items()):
        if n.startswith("test_"):
            f()
            print("ok", n)
