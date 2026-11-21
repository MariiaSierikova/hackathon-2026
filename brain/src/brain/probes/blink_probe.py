"""Blink probe: LogiInsight face mesh -> blink detection -> trigger to the Logi plugin.

Question it answers: can the camera's own AI give us blinks fast enough to react to?
Each detected blink POSTs a trigger to http://127.0.0.1:8787/trigger (default: test_blink = one soft mouse tap).
Prints live eye openness (EAR), blink count and the real AI result rate so we can judge quality.

Run (credentials come from ~/.config/workhealth/camera.env, never from code):
    cd brain && uv run python src/brain/probes/blink_probe.py --device 0 --seconds 90
"""
import argparse
import collections
import json
import statistics
import sys
import threading
import time
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve()
sys.path.insert(0, str(HERE.parents[2]))                       # brain/src
sys.path.insert(0, str(HERE.parents[3] / "vendor" / "logiinsight"))

from brain.camconfig import load_camera_env  # noqa: E402

from brain.vision.blink import EYES, BlinkDetector, ear  # noqa: E402


def post_trigger(trigger, severity=1.0):
    req = urllib.request.Request(
        "http://127.0.0.1:8787/trigger",
        data=json.dumps({"trigger": trigger, "severity": severity}).encode(),
        headers={"Content-Type": "application/json"}, method="POST")
    try:
        urllib.request.urlopen(req, timeout=1).read()
        return True
    except Exception as e:  # plugin not running etc.
        print(f"  (trigger not delivered: {e})")
        return False


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--device", default=None, help="camera index of 'LVNext Camera' (RGB); try 0,1,2 if wrong")
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    ap.add_argument("--format", default="MJPG")
    ap.add_argument("--seconds", type=int, default=90, help="run time; keep under ~10 min (camera heats)")
    ap.add_argument("--trigger", default="test_blink", help="trigger sent to the plugin on each blink")
    ap.add_argument("--no-send", action="store_true", help="only print, don't send triggers")
    ap.add_argument("--ratio", type=float, default=0.72, help="closed if EAR < ratio * open level")
    ap.add_argument("--ip", default="192.168.1.10")
    a = ap.parse_args()

    if not load_camera_env():
        sys.exit("Camera credentials not found. See docs/CAMERA_TEST.md step 3.")

    import cv2
    from logiwebcam import LogiWebcamClient
    from rgb_ai_capture import open_uvc

    print("Connecting to camera AI ...")
    cam = LogiWebcamClient(ip=a.ip)
    cam.connect()
    print("  firmware:", cam.get_firmware_version())
    cam.set_ai_config(enable_face_detection=True, enable_face_mesh=True)
    time.sleep(1.0)

    print("Opening RGB video (keeps the AI running) ...")
    cap, w, h = open_uvc(a.width, a.height, a.device, a.format)
    stop = threading.Event()

    def reader():                                      # the AI only runs while someone reads the video
        while not stop.is_set():
            cap.read()

    threading.Thread(target=reader, daemon=True).start()

    det = BlinkDetector(ratio=a.ratio)
    last_ts, results, mesh_missing, blinks = None, 0, 0, 0
    t0 = time.time()
    t_report = t0
    blink_times = []
    ear_log = collections.deque(maxlen=400)
    print(f"\nRunning {a.seconds}s. Blink normally. Ctrl+C to stop.\n")
    try:
        while time.time() - t0 < a.seconds:
            data = cam.get_latest_ai_data()
            now = time.time()
            if data and data.get("faces"):
                ts = data.get("timestamp_ms")
                if ts != last_ts:
                    last_ts = ts
                    results += 1
                    lm = data["faces"][0].get("landmarks") or []
                    if len(lm) >= 400:
                        e = statistics.mean(v for v in (ear(lm, EYES["right"]), ear(lm, EYES["left"])) if v)
                        ear_log.append((now, e))
                        dur = det.update(e, now)
                        if dur is not None:
                            blinks += 1
                            blink_times.append(now)
                            print(f"  BLINK #{blinks}  closed {dur:4.0f} ms   EAR={e:.2f}")
                            if not a.no_send:
                                post_trigger(a.trigger)
                    else:
                        mesh_missing += 1
            if now - t_report >= 5:
                span = now - t_report
                base = det.baseline()
                print(f"[{int(now - t0):3d}s] distinct AI results/s: {results / (now - t0):4.1f} | "
                      f"mesh missing: {mesh_missing} | open-eye EAR: {base:.2f}" if base else
                      f"[{int(now - t0):3d}s] distinct AI results/s: {results / (now - t0):4.1f} | learning baseline ...")
                t_report = now
    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        time.sleep(0.2)
        cap.release()
        try:
            cam.set_ai_config(enable_face_detection=False, enable_face_mesh=False)  # let the camera cool
        except Exception:
            pass
        cam.disconnect()

    dt = time.time() - t0
    print(f"\nSummary: {blinks} blinks in {dt:.0f}s = {blinks / dt * 60:.1f}/min | "
          f"AI results {results} ({results / dt:.1f}/s) | mesh missing in {mesh_missing} results")


if __name__ == "__main__":
    main()
