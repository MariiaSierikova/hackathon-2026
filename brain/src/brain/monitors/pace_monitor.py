"""Pace monitor: listens to the microphone and tells the Logi plugin when you speak too fast.

 - syllables/s >= 5.0 (and speaking)  -> pace_too_fast  (snail on screen + on the Keypad, then a calm mouse vibration)
 - back below 4.3 or silence          -> pace_ok        (snail goes away)
 - speech level < -25 dBFS            -> speak_up       (yellow duck on screen + mouse tap), back above -22 -> speak_ok
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
from brain.audio.loudness import OK_DBFS, QUIET_DBFS, LoudnessState  # noqa: E402
from brain.bridge.client import PACE_TOO_FAST, SPEAK_OK, SPEAK_UP, CoachClient  # noqa: E402
from brain.settings import calibrate_volume, page_db_to_dbfs, page_thresholds, wpm_to_syllables  # noqa: E402


def add_args(ap):
    ap.add_argument("--mic", default=None, help="microphone number (see: app.py --list). Default: the camera microphone, else the default one")
    ap.add_argument("--fast-rate", type=float, default=None, help=f"syllables/s that count as too fast. Default: the settings page's 'too fast' slider, else {RATE_THRESHOLD}")
    ap.add_argument("--ok-rate", type=float, default=None, help=f"back below this = pace fine again. Default: 0.7 below the fast limit (built-in {RATE_RESET})")
    ap.add_argument("--quiet-db", type=float, default=None, help=f"speech level (dBFS) below which you are too quiet. Default: the page's 'Volume floor' slider, else {QUIET_DBFS}")
    ap.add_argument("--ok-db", type=float, default=None, help=f"back above this = loud enough again. Default: 3 dB above the quiet limit (built-in {OK_DBFS})")
    ap.add_argument("--calibrate-volume", action="store_true", help="speak normally for 10 s; the page's 'Volume floor' slider is set 10 dB below your normal level, then exit")
    ap.add_argument("--force", action="store_true", help="demo: the plugin skips its notification rules (cooldown etc.)")
    ap.add_argument("--simulate", action="store_true", help="NO microphone: fake talker, normal -> too fast (26-42 s) -> normal")


def thresholds(a, page):
    """CLI flag wins, then the settings page, then the built-in default. Returns (fast, ok_rate, quiet_dbfs, ok_dbfs, where)."""
    where = []
    if a.fast_rate is not None:
        fast = a.fast_rate
    elif "fast_wpm" in page:
        fast = wpm_to_syllables(page["fast_wpm"])
        where.append(f"too fast above {page['fast_wpm']:.0f} wpm (page)")
    else:
        fast = RATE_THRESHOLD
    ok_rate = a.ok_rate if a.ok_rate is not None else (max(0.5, fast - (RATE_THRESHOLD - RATE_RESET)) if "fast_wpm" in page else RATE_RESET)
    if a.quiet_db is not None:
        quiet = a.quiet_db
    elif "quiet_db" in page:
        quiet = page_db_to_dbfs(page["quiet_db"])
        where.append(f"voice floor {page['quiet_db']:.0f} dB = {quiet:.0f} dBFS (page)")
    else:
        quiet = QUIET_DBFS
    ok_db = a.ok_db if a.ok_db is not None else (quiet + (OK_DBFS - QUIET_DBFS))
    return fast, ok_rate, quiet, ok_db, where


def run(a, stop, say=print):
    coach = CoachClient(min_repeat_s=8.0 if a.force else 2.0)
    if not coach.health():
        say("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787 (Is Logi Options+ running with the plugin?)")

    if a.simulate:
        src = FakeSource()
        say("SIMULATION: talks normally, TOO QUIET from 8 s to 22 s (duck), TOO FAST from 26 s to 42 s (snail), then normal. "
            "Expected: the snail appears at 26 s, the mouse buzzes ~5 s later, the snail leaves at ~42 s.")
    else:
        try:
            src = MicSource(a.mic, say)
        except Exception as e:
            say(f"ERROR: cannot open the microphone: {e}")
            return

    if getattr(a, "calibrate_volume", False):
        try:
            calibrate_volume(coach, src, say)
        finally:
            src.close()
        return

    fast_rate, ok_rate, quiet_db, ok_db, where = thresholds(a, page_thresholds(coach))
    state = PaceState(fast_rate, ok_rate)
    loud = LoudnessState(quiet_db, ok_db)
    shown = (round(fast_rate, 3), round(quiet_db, 1))
    say(f"limits: too fast above {fast_rate:.2f} syllables/s ({syllables_to_wpm(fast_rate):.0f} wpm), too quiet below {quiet_db:.1f} dBFS"
        + (f"   [{'; '.join(where)}]" if where else "   [built-in defaults: open the settings page once to use its sliders]"))
    was_fast = was_quiet = False
    t0 = time.time()
    t_status = 0.0
    t_page = time.time()
    sent = 0
    try:
        while not stop.is_set():
            if a.simulate and src.finished:
                break
            if time.time() - t_page >= 3.0:                      # follow the sliders while running
                t_page = time.time()
                fr, orr, qd, od, where = thresholds(a, page_thresholds(coach))
                state.fast_rate, state.ok_rate, loud.quiet_db, loud.ok_db = fr, orr, qd, od
                if (round(fr, 3), round(qd, 1)) != shown:
                    shown = (round(fr, 3), round(qd, 1))
                    say(f"[{time.time() - t0:5.1f}s] settings page changed: too fast above {fr:.2f} syllables/s ({syllables_to_wpm(fr):.0f} wpm), "
                        f"too quiet below {qd:.1f} dBFS")
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
            quiet = loud.update(src.loudness, speaking)
            if quiet:
                if coach.trigger(SPEAK_UP, loud.severity(src.loudness), force=a.force):
                    say(f"[{time.time() - t0:5.1f}s] >> too quiet: {src.loudness:.1f} dBFS, speak_up severity {loud.severity(src.loudness):.2f} sent")
            elif was_quiet:
                coach.trigger(SPEAK_OK, 1.0)
                say(f"[{time.time() - t0:5.1f}s] voice is loud enough again -> speak_ok")
            was_quiet = quiet
            now = time.time()
            if now - t_status >= 3:
                t_status = now
                lv = f"{src.loudness:5.1f} dBFS" if src.loudness is not None else "  -    dBFS"
                say(f"[{int(now - t0):3d}s] rate: {rate:4.1f} syllables/s | speaking: {'yes' if speaking else 'no '} | "
                    f"{'TOO FAST' if fast else 'ok      '} | level {lv} {'TOO QUIET' if quiet else 'ok       '} | plugin: {coach.sent} ok / {coach.failed} failed")
            stop.wait(0.5)
    finally:
        src.close()
        coach.pace_ok()
        coach.trigger(SPEAK_OK, 1.0)
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
