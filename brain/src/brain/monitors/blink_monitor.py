"""Blink monitor: watches you through the LogiInsight camera and tells the Logi plugin.

 - every detected blink          -> blink_detected  (ends the owl / blinking-eye reminder)
 - no blink for --no-blink-after -> blink_low        (owl on screen + eye on the Keypad + mouse tap)

Needs the Logi plugin running (and optionally overlay/overlay.py for the owl). Credentials: environment variables
LOGIWEBCAM_USER / LOGIWEBCAM_PASSWORD (or ~/.config/workhealth/camera.env).

    cd brain
    uv run python src/brain/monitors/blink_monitor.py --device N --no-blink-after 6 --force
"""
import argparse
import statistics  # noqa: F401  (kept for quick experiments)
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve()
sys.path.insert(0, str(HERE.parents[2]))                       # brain/src
sys.path.insert(0, str(HERE.parents[3] / "vendor" / "logiinsight"))

from brain.bridge.client import BLINK_LOW, CoachClient  # noqa: E402
from brain.camconfig import load_camera_env  # noqa: E402
from brain.vision.blink import BlinkDetector, NoBlinkWatcher, mean_ear  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--device", default=None, help="camera number of the colour 'LVNext Camera' (see CAMERA_TEST.md A9)")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--format", default="MJPG")
    ap.add_argument("--ip", default="192.168.1.10")
    ap.add_argument("--no-blink-after", type=float, default=8.0, help="seconds without a blink before blink_low (demo: 5-6)")
    ap.add_argument("--minutes", type=float, default=8.0, help="stop after this long: the camera AI overheats after ~10 min")
    ap.add_argument("--force", action="store_true", help="demo: skip the notification policy (cooldown etc.)")
    ap.add_argument("--ratio", type=float, default=0.72, help="eye counts as closed below ratio * open level")
    a = ap.parse_args()

    if not load_camera_env():
        sys.exit("Camera credentials not found: set LOGIWEBCAM_USER and LOGIWEBCAM_PASSWORD (docs/CAMERA_TEST.md A7).")

    coach = CoachClient(min_repeat_s=0.4 if a.force else 2.0)
    if not coach.health():
        print("WARNING: the Logi plugin does not answer on 127.0.0.1:8787 (blinks are only printed). "
              "Is Logi Options+ running with the WorkHealthCoach plugin?")

    from logiwebcam import LogiWebcamClient
    from rgb_ai_capture import open_uvc

    print("Connecting to camera AI ...")
    cam = LogiWebcamClient(ip=a.ip)
    cam.connect()
    cam.set_ai_config(enable_face_detection=True, enable_face_mesh=True)
    time.sleep(1.0)
    print("Opening the colour video (keeps the AI running) ...")
    cap, _, _ = open_uvc(a.width, a.height, a.device, a.format)
    stop = threading.Event()

    def reader():                       # the camera AI only runs while someone reads the video
        while not stop.is_set():
            cap.read()

    threading.Thread(target=reader, daemon=True).start()

    det = BlinkDetector(ratio=a.ratio)
    watcher = NoBlinkWatcher(after_s=a.no_blink_after)
    last_ts, blinks, alerts = None, 0, 0
    t0 = time.time()
    t_status = t0
    print(f"\nWatching for {a.minutes:.0f} min. Blink normally; stare at the screen for ~{a.no_blink_after:.0f}s to see the owl. "
          "Ctrl+C to stop.\n")
    try:
        while time.time() - t0 < a.minutes * 60:
            data = cam.get_latest_ai_data()
            now = time.time()
            faces = data.get("faces") if data else None
            if faces:
                watcher.face_seen(now)
                ts = data.get("timestamp_ms")
                if ts != last_ts:
                    last_ts = ts
                    e = mean_ear(faces[0].get("landmarks"))
                    if e is not None:
                        dur = det.update(e, now)
                        if dur is not None:
                            blinks += 1
                            watcher.blink(now)
                            coach.blink_detected()
                            print(f"  blink #{blinks} ({dur:.0f} ms)")
            sev = watcher.severity(now)
            if sev is not None:
                if coach.trigger(BLINK_LOW, sev, force=a.force):
                    alerts += 1
                    print(f"  >> no blink for {now - watcher.last_blink:.0f}s: blink_low severity {sev:.2f} sent")
            if now - t_status >= 5:
                t_status = now
                base = det.baseline()
                gap = (now - watcher.last_blink) if watcher.last_blink else 0
                print(f"[{int(now - t0):3d}s] face: {'yes' if faces else 'NO '} | since last blink: {gap:4.1f}s | "
                      f"blinks: {blinks} | open-eye EAR: {f'{base:.2f}' if base else 'learning'}")
            time.sleep(0.02)
    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        time.sleep(0.2)
        cap.release()
        try:
            cam.set_ai_config(enable_face_detection=False, enable_face_mesh=False)   # let the camera cool down
        except Exception:
            pass
        cam.disconnect()
        coach.blink_detected()           # leave the screen clean
    dt = time.time() - t0
    print(f"\nDone: {blinks} blinks in {dt:.0f}s ({blinks / dt * 60:.1f}/min), {alerts} blink_low reminders sent.")


if __name__ == "__main__":
    main()
