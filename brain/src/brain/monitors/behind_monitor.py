"""Person-behind monitor: looks for a SECOND face with the LogiInsight camera's INFRARED stream and tells the plugin.

Why the infrared stream: the Gaze Tracker app already reads the colour camera, and the camera's AI reports only one face. The infrared
camera ("LVNext CPD Camera", grey and portrait) is a separate device that nothing else uses, and it works in a dark room too.
The biggest face is YOU; every other face is sent to the plugin (POST /behind) with a stable id, its x position and its size. The plugin
decides: walks past = ignored, comes to your desk / stays behind you = mouse buzz, Keypad alert, screen note.

    cd brain
    uv run python src/brain/app.py --list                               # camera numbers: the grey and tall one is infrared
    uv run python src/brain/monitors/behind_monitor.py --simulate       # NO camera: a made-up visitor
    uv run python src/brain/monitors/behind_monitor.py --ir-device 2 --preview
    uv run python src/brain/monitors/behind_monitor.py --image photo.png   # one picture, prints what it finds
"""
import argparse
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve()
sys.path.insert(0, str(HERE.parents[2]))                       # brain/src
sys.path.insert(0, str(HERE.parents[3] / "vendor" / "logiinsight"))

from brain.bridge.client import CoachClient  # noqa: E402
from brain.vision.faces import Face, IdTracker, split_user_and_others, to_faces  # noqa: E402

CASCADE = HERE.parents[3] / "vendor" / "haar" / "haarcascade_frontalface_alt2.xml"


def add_args(ap):
    ap.add_argument("--ir-device", default=None, help="number of the INFRARED camera (grey, tall) from: app.py --list. Default: find it")
    ap.add_argument("--ir-width", type=int, default=512)
    ap.add_argument("--ir-height", type=int, default=640)
    ap.add_argument("--behind-every", type=float, default=0.5, help="seconds between face searches")
    ap.add_argument("--min-face", type=int, default=24, help="smallest face (pixels wide) to look for; smaller = sees people further away, more false alarms")
    ap.add_argument("--preview", action="store_true", help="show the camera picture with boxes (green = you, red = others); Esc closes it")
    ap.add_argument("--image", default=None, help="NO camera: look at one picture file, print the faces and exit")


def load_cascade():
    import cv2
    c = cv2.CascadeClassifier(str(CASCADE))
    if c.empty():
        raise RuntimeError(f"cannot load the face model {CASCADE}")
    return c


def detect(gray, cascade, min_face=24):
    """Boxes (x, y, w, h) of frontal faces in a grey picture. Histogram equalisation helps the dim infrared picture."""
    import cv2
    g = cv2.equalizeHist(gray)
    boxes = cascade.detectMultiScale(g, scaleFactor=1.1, minNeighbors=5, minSize=(min_face, min_face))
    return [tuple(int(v) for v in b) for b in boxes]


class FakeVisitor:
    """--simulate: nobody (0-4 s), someone comes closer from the side (4-10 s), stands (10-16 s), leaves (16-20 s), then nobody."""
    DURATION = 24

    def __init__(self):
        self.t0 = time.time()

    @property
    def finished(self):
        return time.time() - self.t0 > self.DURATION

    def faces(self):
        t = time.time() - self.t0
        you = Face(0.5, 0.30)
        if 4 <= t < 10:
            return [you, Face(0.78 - 0.05 * (t - 4), 0.05 + 0.014 * (t - 4))]      # comes closer
        if 10 <= t < 16:
            return [you, Face(0.60, 0.13)]                                          # stands behind you
        if 16 <= t < 20:
            return [you, Face(0.60 + 0.05 * (t - 16), 0.13 - 0.02 * (t - 16))]      # leaves
        return [you]


def run(a, stop, say=print):
    coach = CoachClient(min_repeat_s=0.0)
    if not coach.health():
        say("WARNING: the Logi plugin does NOT answer on 127.0.0.1:8787, samples are only printed.")
    tracker = IdTracker()
    t0 = time.time()

    def handle(faces, now, label=""):
        user, others = split_user_and_others(faces)
        tracked = tracker.assign(others, now)
        if tracked:
            coach.behind([{"id": tid, "x": round(f.x, 3), "size": round(f.size, 3)} for tid, f in tracked])
        return user, tracked

    # ---- one picture, no camera ----
    if a.image:
        import cv2
        img = cv2.imread(a.image, cv2.IMREAD_GRAYSCALE)
        if img is None:
            say(f"ERROR: cannot read {a.image}")
            return
        boxes = detect(img, load_cascade(), a.min_face)
        faces = to_faces(boxes, img.shape[1])
        user, others = split_user_and_others(faces)
        say(f"{len(boxes)} face(s) found in {a.image} ({img.shape[1]}x{img.shape[0]}): {boxes}")
        say(f"you (biggest): {user}; others: {others}")
        return

    # ---- simulation ----
    if a.simulate:
        src = FakeVisitor()
        say("SIMULATION: nobody 0-4 s, a visitor comes closer 4-10 s, stands behind you 10-16 s, leaves 16-20 s. Expected: one alert around 6-8 s "
            "(mouse buzz + Keypad), no second alert for the same visit.")
        last = 0.0
        while not stop.is_set() and not src.finished:
            now = time.time()
            if now - last >= a.behind_every:
                last = now
                user, tracked = handle(src.faces(), now)
                if tracked:
                    say(f"[{now - t0:5.1f}s] others: " + ", ".join(f"#{i} x={f.x:.2f} size={f.size:.3f}" for i, f in tracked) + " -> plugin")
            time.sleep(0.05)
        return

    # ---- real infrared camera ----
    import cv2
    from camera_utils import CameraNotFoundError, backend_id, find_camera, open_camera
    try:
        cascade = load_cascade()
        if a.ir_device is not None:
            idx, backend = int(a.ir_device), None
        else:
            idx, name = find_camera("ir")
            backend = backend_id(name)
        cap, info = open_camera(idx, backend, a.ir_width, a.ir_height, "MJPG", 30)
    except (CameraNotFoundError, RuntimeError) as e:
        say(f"ERROR: {e}")
        return
    say(f"infrared camera {idx}: {info['width']}x{info['height']} {info['fourcc']} {info['fps']:.0f} fps. Looking for a second face every "
        f"{a.behind_every:.1f} s (smallest face {a.min_face} px).")
    if info["height"] <= info["width"]:
        say("WARNING: this picture is not tall (portrait), so it may be the COLOUR camera, which the Gaze Tracker uses. Check the number with: app.py --list")
    for _ in range(20):                      # the first ~20 frames are an auto-exposure ramp
        cap.read()

    last, t_status, frames, found = 0.0, time.time(), 0, 0
    try:
        while not stop.is_set():
            ok, frame = cap.read()
            if not ok or frame is None:
                time.sleep(0.05)
                continue
            frames += 1
            now = time.time()
            if now - last < a.behind_every:
                continue
            last = now
            gray = frame if frame.ndim == 2 else cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            boxes = detect(gray, cascade, a.min_face)
            faces = to_faces(boxes, gray.shape[1])
            user, tracked = handle(faces, now)
            found += len(boxes)
            if a.preview:
                shown = cv2.cvtColor(gray, cv2.COLOR_GRAY2BGR)
                ordered = sorted(boxes, key=lambda b: b[2], reverse=True)
                for k, (x, y, w, h) in enumerate(ordered):
                    cv2.rectangle(shown, (x, y), (x + w, y + h), (0, 200, 0) if k == 0 else (0, 0, 255), 2)
                cv2.imshow("person behind (Esc closes)", shown)
                if cv2.waitKey(1) == 27:
                    stop.set()
            if now - t_status >= 5:
                t_status = now
                say(f"[{now - t0:4.0f}s] faces now: {len(boxes)} ({'you + ' + str(len(tracked)) + ' other' if user and tracked else 'you only' if user else 'nobody'}) "
                    f"| frames {frames} | plugin: {coach.sent} ok / {coach.failed} failed"
                    + (f" | other: x={tracked[0][1].x:.2f} size={tracked[0][1].size:.3f}" if tracked else ""))
    finally:
        cap.release()
        if a.preview:
            cv2.destroyAllWindows()
        say("Person-behind monitor stopped.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_args(ap)
    ap.add_argument("--simulate", action="store_true", help="NO camera: a made-up visitor")
    a = ap.parse_args()
    stop = threading.Event()
    try:
        run(a, stop, say=lambda m: print(m, flush=True))
    except KeyboardInterrupt:
        stop.set()


if __name__ == "__main__":
    main()
