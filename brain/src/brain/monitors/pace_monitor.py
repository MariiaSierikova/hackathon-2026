"""Pace monitor: listens to the microphone and tells the Logi plugin when you speak too fast.

 - syllables/s >= 5.0 (and speaking)  -> pace_too_fast  (snail on screen + on the Keypad, then a calm mouse vibration)
 - back below 4.3 or silence          -> pace_ok        (snail goes away)
Detection = the teammate's speech_flagger.py (brain/audio/pace.py). The Keypad also shows an approximate words/minute.

    cd brain
    uv run python src/brain/monitors/pace_monitor.py --simulate        # NO microphone: fake talker, normal -> fast -> normal
    uv run python src/brain/monitors/pace_monitor.py --mic 11 --force   # real microphone (number: see --list in app.py)
"""
import argparse
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve()
sys.path.insert(0, str(HERE.parents[2]))                       # brain/src
sys.path.insert(0, str(HERE.parents[3] / "vendor" / "logiinsight"))

from brain.audio.pace import RATE_RESET, RATE_THRESHOLD, FakeSource, MicSource, PaceState, syllables_to_wpm  # noqa: E402
from brain.bridge.client import PACE_TOO_FAST, CoachClient  # noqa: E402


def add_args(ap):
    ap.add_argument("--mic", default=None, help="microphone number (see: app.py --list). Default: the camera microphone, else the default one")
    ap.add_argument("--fast-rate", type=float, default=RATE_THRESHOLD, help=f"syllables/s that count as too fast (default {RATE_THRESHOLD})")
    ap.add_argument("--ok-rate", type=float, default=RATE_RESET, help=f"back below this = pace fine again (default {RATE_RESET})")
    ap.add_argument("--force", action="store_true", help="demo: the plugin skips its notification rules (cooldown etc.)")
    ap.add_argument("--simulate", action="store_true", help="NO microphone: fake talker, normal -> too fast (26-42 s) -> normal")


def run(a, stop, say=print):
    coach = CoachClient(min_repeat_s=8.0 if a.force else 2.0)
    if not coach.health():
        say("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787 (Is Logi Options+ running with the plugin?)")

    if a.simulate:
        src = FakeSource()
        say("SIMULATION: talks normally, then TOO FAST from 26 s to 42 s, then normal again. "
            "Expected: the snail appears at 26 s, the mouse buzzes ~5 s later, the snail leaves at ~42 s.")
    else:
        try:
            src = MicSource(a.mic, say)
        except Exception as e:
            say(f"ERROR: cannot open the microphone: {e}")
            return

    state = PaceState(a.fast_rate, a.ok_rate)
    was_fast = False
    t0 = time.time()
    t_status = 0.0
    sent = 0
    try:
        while not stop.is_set():
            if a.simulate and src.finished:
                break
            rate, speaking = src.read()
            fast = state.update(rate, speaking)
            if speaking:
                coach.wpm(syllables_to_wpm(rate))
            if fast:
                if coach.trigger(PACE_TOO_FAST, state.severity(rate), force=a.force):
                    sent += 1
                    say(f"[{time.time() - t0:5.1f}s] >> too fast: {rate:.1f} syllables/s, pace_too_fast severity {state.severity(rate):.2f} sent")
            elif was_fast:
                coach.pace_ok()
                say(f"[{time.time() - t0:5.1f}s] pace is fine again ({rate:.1f} syllables/s) -> pace_ok")
            was_fast = fast
            now = time.time()
            if now - t_status >= 3:
                t_status = now
                say(f"[{int(now - t0):3d}s] rate: {rate:4.1f} syllables/s | speaking: {'yes' if speaking else 'no '} | "
                    f"{'TOO FAST' if fast else 'ok      '} | plugin: {coach.sent} ok / {coach.failed} failed")
            stop.wait(0.5)
    finally:
        src.close()
        coach.pace_ok()
        time.sleep(0.3)
    say(f"Done: {sent} pace_too_fast messages sent, plugin accepted {coach.sent}, failed {coach.failed}"
        + (f" (last error: {coach.last_error})" if coach.failed else "") + ".")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_args(ap)
    a = ap.parse_args()
    stop = threading.Event()
    try:
        run(a, stop, say=lambda m: print(m, flush=True))
    except KeyboardInterrupt:
        stop.set()


if __name__ == "__main__":
    main()
