"""Blink monitor: watches you through the LogiInsight camera and tells the Logi plugin.

 - every detected blink          -> blink_detected  (ends the owl / blinking-eye reminder)
 - no blink for --no-blink-after -> blink_low        (owl on screen + eye on the Keypad + mouse tap)

Needs the Logi plugin running (and overlay/overlay.py for the owl). Credentials: environment variables
LOGIWEBCAM_USER / LOGIWEBCAM_PASSWORD (or ~/.config/workhealth/camera.env).

    cd brain
    uv run python src/brain/monitors/blink_monitor.py --simulate --no-blink-after 5      # NO camera: tests plugin + owl
    uv run python src/brain/monitors/blink_monitor.py --device N --no-blink-after 6 --force --log ear_log.csv
"""
import argparse
import random
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


class FakeCam:
    """Stands in for the camera with --simulate: a face that blinks at chosen times, 27 results per second."""

    RIGHT = {"outer": 33, "up1": 160, "up2": 158, "inner": 133, "low2": 153, "low1": 144}
    LEFT = {"outer": 362, "up1": 385, "up2": 387, "inner": 263, "low2": 373, "low1": 380}

    def __init__(self, blink_times, duration):
        self.blink_times, self.duration = blink_times, duration
        self.t0 = time.time()
        self.finished = False

    def connect(self): pass
    def disconnect(self): pass
    def set_ai_config(self, **kw): pass
    def get_firmware_version(self): return "simulated camera"

    def _landmarks(self, closed):
        lm = [[0.0, 0.0, 0.0] for _ in range(478)]
        h = (3.6 if closed else 18.0) + random.uniform(-0.6, 0.6)          # EAR = h / 60 -> open 0.30, closed 0.06
        for eye, x0 in ((self.RIGHT, 100.0), (self.LEFT, 260.0)):
            lm[eye["outer"]] = [x0, 200.0, 0.0]
            lm[eye["inner"]] = [x0 + 60.0, 200.0, 0.0]
            lm[eye["up1"]] = [x0 + 15.0, 200.0 - h / 2, 0.0]
            lm[eye["up2"]] = [x0 + 45.0, 200.0 - h / 2, 0.0]
            lm[eye["low1"]] = [x0 + 15.0, 200.0 + h / 2, 0.0]
            lm[eye["low2"]] = [x0 + 45.0, 200.0 + h / 2, 0.0]
        return lm

    def get_latest_ai_data(self):
        t = time.time() - self.t0
        if t > self.duration:
            self.finished = True
        closed = any(b <= t < b + 0.14 for b in self.blink_times)
        n = int(t * 27)
        return {"timestamp_ms": n * 37, "faces": [{"bbox": [0, 0, 1, 1], "landmarks": self._landmarks(closed)}]}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--device", default=None, help="camera number of the colour 'LVNext Camera' (see CAMERA_TEST.md A9)")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--format", default="MJPG")
    ap.add_argument("--ip", default="192.168.1.10")
    ap.add_argument("--no-blink-after", type=float, default=8.0, help="seconds without a blink before blink_low (demo: 5-6)")
    ap.add_argument("--minutes", type=float, default=8.0, help="stop after this long: the camera AI overheats after ~10 min")
    ap.add_argument("--force", action="store_true", help="demo: the plugin skips its notification rules (cooldown etc.)")
    ap.add_argument("--ratio", type=float, default=0.72, help="eye counts as closed below ratio * open level (higher = more sensitive)")
    ap.add_argument("--min-samples", type=int, default=2, help="results the eye must stay closed to count as a blink")
    ap.add_argument("--log", default=None, help="write every result (time, EAR, baseline, blink) to this CSV file, for tuning")
    ap.add_argument("--simulate", action="store_true", help="NO camera: fake face that blinks, then stares, then blinks again (30 s)")
    a = ap.parse_args()

    coach = CoachClient(min_repeat_s=2.0)
    plugin_ok = coach.health()
    if plugin_ok:
        print(f"Plugin answers (build {getattr(coach, 'build', '?')}).")
    else:
        print("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787, blinks will only be printed. "
              "Is Logi Options+ running with the WorkHealthCoach plugin? (uv run python ..\\scripts\\trigger.py --list)")

    cap = None
    stop = threading.Event()
    if a.simulate:
        cam = FakeCam(blink_times=[3, 6, 20, 23, 26], duration=30)
        print("SIMULATION: fake face, blinks at 3 s and 6 s, then STARES until 20 s, then blinks again.\n"
              f"Expected: the owl appears about {6 + a.no_blink_after:.0f} s in and disappears at 20 s.")
    else:
        if not load_camera_env():
            sys.exit("Camera credentials not found: set LOGIWEBCAM_USER and LOGIWEBCAM_PASSWORD (docs/CAMERA_TEST.md A7).")
        from logiwebcam import LogiWebcamClient
        from rgb_ai_capture import open_uvc

        print("Connecting to camera AI ...")
        cam = LogiWebcamClient(ip=a.ip)
        cam.connect()
        cam.set_ai_config(enable_face_detection=True, enable_face_mesh=True)
        time.sleep(1.0)
        print("Opening the colour video (keeps the AI running) ...")
        cap, _, _ = open_uvc(a.width, a.height, a.device, a.format)

        def reader():                       # the camera AI only runs while someone reads the video
            while not stop.is_set():
                cap.read()

        threading.Thread(target=reader, daemon=True).start()

    det = BlinkDetector(ratio=a.ratio, min_samples=a.min_samples)
    watcher = NoBlinkWatcher(after_s=a.no_blink_after)
    log = open(a.log, "w", buffering=1) if a.log else None
    if log:
        log.write("t_s,ear,baseline,closed,blink,since_last_blink_s\n")
    last_ts, blinks, alerts, no_mesh = None, 0, 0, 0
    t0 = time.time()
    t_status = t0
    if not a.simulate:
        print(f"\nWatching for {a.minutes:.0f} min. Blink normally; stare at the screen for ~{a.no_blink_after:.0f}s to see the owl. "
              "Ctrl+C to stop.\n")
    try:
        while True:
            if a.simulate and cam.finished:
                break
            if not a.simulate and time.time() - t0 >= a.minutes * 60:
                break
            data = cam.get_latest_ai_data()
            now = time.time()
            faces = data.get("faces") if data else None
            if faces:
                watcher.face_seen(now)
                ts = data.get("timestamp_ms")
                if ts != last_ts:
                    last_ts = ts
                    e = mean_ear(faces[0].get("landmarks"))
                    if e is None:
                        no_mesh += 1
                    else:
                        dur = det.update(e, now)
                        if dur is not None:
                            blinks += 1
                            watcher.blink(now)
                            coach.blink_detected()
                            print(f"  [{now - t0:5.1f}s] blink #{blinks} ({dur:.0f} ms)")
                        if log:
                            base = det.baseline()
                            log.write(f"{now - t0:.3f},{e:.4f},{base if base else ''},{int(det.closed)},{int(dur is not None)},"
                                      f"{now - watcher.last_blink:.2f}\n")
            sev = watcher.severity(now)
            if sev is not None and coach.trigger(BLINK_LOW, sev, force=a.force):
                alerts += 1
                print(f"  [{now - t0:5.1f}s] >> no blink for {now - watcher.last_blink:.0f}s: blink_low severity {sev:.2f} sent")
            if now - t_status >= 5:
                t_status = now
                base = det.baseline()
                gap = (now - watcher.last_blink) if watcher.last_blink else 0
                print(f"[{int(now - t0):3d}s] face: {'yes' if faces else 'NO '} | since last blink: {gap:4.1f}s | blinks: {blinks} | "
                      f"open-eye EAR: {f'{base:.2f}' if base else 'learning'} | no-mesh results: {no_mesh} | "
                      f"plugin: {coach.sent} ok / {coach.failed} failed")
            time.sleep(0.02)
    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        time.sleep(0.2)
        if cap is not None:
            cap.release()
        try:
            cam.set_ai_config(enable_face_detection=False, enable_face_mesh=False)   # let the camera cool down
        except Exception:
            pass
        cam.disconnect()
        coach.blink_detected()           # leave the screen clean
        time.sleep(0.3)
        if log:
            log.close()
    dt = time.time() - t0
    print(f"\nDone: {blinks} blinks in {dt:.0f}s ({blinks / dt * 60:.1f}/min), {alerts} blink_low reminders sent, "
          f"plugin accepted {coach.sent}, failed {coach.failed}" + (f" (last error: {coach.last_error})" if coach.failed else "") + ".")


if __name__ == "__main__":
    main()
