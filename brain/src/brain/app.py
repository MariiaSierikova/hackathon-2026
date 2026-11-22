"""ONE command that runs everything on the laptop: owl + snail on screen, blink watching (camera), pace watching (microphone).

    cd brain
    uv run python src/brain/app.py --list                                 # camera and microphone numbers
    uv run python src/brain/app.py --simulate --no-blink-after 5          # NO hardware: fake blinks + fake talker (50 s)
    uv run python src/brain/app.py --device 0 --mic 11 --force            # real camera + microphone, demo mode

The Logi plugin (Logi Options+) must be running: it decides what reaches the mouse and the Keypad.
Press Ctrl+C in this window to stop everything.
"""
import argparse
import signal
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve()
ROOT = HERE.parents[3]
sys.path.insert(0, str(HERE.parents[1]))                       # brain/src
sys.path.insert(0, str(ROOT / "brain" / "vendor" / "logiinsight"))
sys.path.insert(0, str(ROOT / "overlay"))

from brain.monitors import blink_monitor, pace_monitor, pomodoro_monitor  # noqa: E402


def list_devices():
    print("MICROPHONES (use the number with --mic; the camera's is 'LVNext Microphone', try the 2-channel entry):")
    try:
        from audio_utils import format_devices, list_audio_devices
        print(format_devices(list_audio_devices()))
    except Exception as e:
        print("  could not list microphones:", e)
    print("\nCAMERAS (use the number with --device; pick the COLOUR one, it must show your face in colour):")
    try:
        from rgb_ai_live import list_uvc_devices
        for idx, backend, w, h in list_uvc_devices():
            print(f"  {idx}: {w}x{h} ({backend})" + ("   <- grey and tall = infrared, NOT this one" if h > w else ""))
    except Exception as e:
        print("  could not list cameras:", e)
    print("\nTip: the camera number that shows a colour picture with the green face mesh is the right one "
          "(docs/CAMERA_TEST.md, step A9).")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="show camera and microphone numbers, then exit")
    ap.add_argument("--no-blink", action="store_true", help="do not watch blinks (no camera)")
    ap.add_argument("--no-pace", action="store_true", help="do not watch speech pace (no microphone)")
    ap.add_argument("--no-overlay", action="store_true", help="do not show the on-screen owl/snail")
    blink_monitor.add_args(ap)
    ap.add_argument("--pomodoro", action="store_true", help="Pomodoro: send desk presence from the Gaze Tracker app to the plugin (the plugin owns the clock) and show the status chip")
    pomodoro_monitor.add_args(ap)
    # pace-only options (the shared ones --force/--simulate are already defined by blink_monitor.add_args)
    ap.add_argument("--mic", default=None, help="microphone number (see --list). Default: the camera microphone, else the default one")
    ap.add_argument("--fast-rate", type=float, default=pace_monitor.RATE_THRESHOLD, help="syllables/s that count as too fast")
    ap.add_argument("--quiet-db", type=float, default=-25.0, help="speech level (dBFS) below which you are too quiet -> duck")
    ap.add_argument("--ok-db", type=float, default=-22.0, help="back above this = loud enough again")
    ap.add_argument("--ok-rate", type=float, default=pace_monitor.RATE_RESET, help="back below this = pace fine again")
    a = ap.parse_args()

    if a.list:
        list_devices()
        return

    if not a.simulate and not a.no_blink and a.device is None:
        sys.exit("Which camera? Run with --list to see the numbers, then add --device N (or use --no-blink to skip the camera, "
                 "or --simulate to test without hardware).")

    stop = threading.Event()
    print_lock = threading.Lock()

    def say_for(tag):
        def say(msg):
            with print_lock:
                print(f"[{tag:5s}] {msg}", flush=True)
        return say

    threads = []
    if not a.no_blink:
        threads.append(threading.Thread(target=blink_monitor.run, args=(a, stop, say_for("blink")), name="blink", daemon=True))
    if not a.no_pace:
        threads.append(threading.Thread(target=pace_monitor.run, args=(a, stop, say_for("pace")), name="pace", daemon=True))
    if a.pomodoro:
        threads.append(threading.Thread(target=pomodoro_monitor.run, args=(a, stop, say_for("pomo")), name="pomo", daemon=True))
    if not threads:
        sys.exit("Nothing to run: you disabled both blink and pace.")

    def finish_when_done():
        for t in threads:
            t.join()
        time.sleep(1.5)
        stop.set()

    if a.no_overlay:
        signal.signal(signal.SIGINT, lambda *_: stop.set())
        for t in threads:
            t.start()
        while not stop.is_set() and any(t.is_alive() for t in threads):
            time.sleep(0.3)
        stop.set()
        return

    import overlay as overlay_module
    from PySide6 import QtCore

    app, _ov = overlay_module.start()
    if a.pomodoro:
        import pomodoro_chip
        chip = pomodoro_chip.Chip(pomodoro_monitor.get_status)   # keep the reference, or Qt deletes it  # noqa: F841
    say_for("app")("Overlay window ready (invisible until the owl or the snail is needed). Ctrl+C stops everything.")
    for t in threads:
        t.start()

    def on_sigint(*_):
        stop.set()
        app.quit()

    signal.signal(signal.SIGINT, on_sigint)
    ticker = QtCore.QTimer()
    ticker.setInterval(300)

    def check():
        if not any(t.is_alive() for t in threads):
            QtCore.QTimer.singleShot(1500, app.quit)   # let the last animal fade, then close
            ticker.stop()

    ticker.timeout.connect(check)
    ticker.start()
    app.exec()
    stop.set()
    for t in threads:
        t.join(timeout=3)


if __name__ == "__main__":
    main()
