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
    ap.add_argument("--scan", action="store_true", help="try every camera number (0-9) and say which ones give a real picture (size, brightness, grey or colour): finds the infrared camera")
    ap.add_argument("--save-frames", default=None, metavar="FOLDER", help="save a picture every 3 s (max 15, with boxes) into FOLDER so you can send them for tuning")
    ap.add_argument("--image", default=None, help="NO camera: look at one picture file, print the faces and exit")


def load_cascade():
    import cv2
    c = cv2.CascadeClassifier(str(CASCADE))
    if c.empty():
        raise RuntimeError(f"cannot load the face model {CASCADE}")
    return c


def enhance(gray):
    """The infrared picture can be VERY dark (brightness 0-3 of 255) while still holding a normal image. Like Logitech's example_IR.py 'stretch'
    mode: map the 1st..99th percentile of the values to 0..255. Returns (brightened picture, dynamic range); range < 2 = no picture at all."""
    import numpy as np
    lo, hi = np.percentile(gray, (1, 99))
    rng = float(hi - lo)
    if rng < 1:
        return gray.copy(), rng
    out = (gray.astype(np.float32) - lo) * (255.0 / rng)
    return np.clip(out, 0, 255).astype(np.uint8), rng


def merge_flicker(prev, cur):
    """The infrared light pulses: lit and unlit frames alternate (Logitech: 'IR picture flickers'). Per pixel the brighter of two consecutive frames
    is always a lit picture (Logitech's example_IR.py --flicker-filter)."""
    import numpy as np
    return cur if prev is None or prev.shape != cur.shape else np.maximum(prev, cur)


def detect(gray, cascade, min_face=24):
    """Boxes (x, y, w, h) of frontal faces in a grey picture (brightened first, see enhance)."""
    import cv2
    g, _ = enhance(gray)
    g = cv2.equalizeHist(g)
    boxes = cascade.detectMultiScale(g, scaleFactor=1.1, minNeighbors=5, minSize=(min_face, min_face))
    if len(boxes) == 0:                      # a washed-out infrared face: local contrast instead of global
        g = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8)).apply(enhance(gray)[0])
        boxes = cascade.detectMultiScale(g, scaleFactor=1.1, minNeighbors=4, minSize=(min_face, min_face))
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


def scan(a, say=print):
    """For every camera number: open it like the monitor does, skip the auto-exposure ramp, then report size, mean brightness and whether it is grey.
    The infrared camera is grey and tall (portrait) and NOT black (mean above ~10)."""
    import cv2
    import numpy as np
    from camera_utils import CameraNotFoundError, open_camera
    say("Scanning camera numbers 0-9 (about 10 s each; keep still, face the camera) ...")
    for idx in range(10):
        try:
            cap, info = open_camera(idx, None, a.ir_width, a.ir_height, "MJPG", 30)
        except CameraNotFoundError:
            say(f"  {idx}: cannot open")
            continue
        try:
            means, last = [], None
            t0 = time.time()
            for _ in range(40):
                ok, frame = cap.read()
                if ok and frame is not None:
                    last = frame
                    means.append(float(frame.mean()))
                if time.time() - t0 > 12:
                    break
            if last is None:
                say(f"  {idx}: opens ({info['width']}x{info['height']}) but delivers NO frames")
                continue
            tail = means[-5:]
            rng = float(enhance(cv2.cvtColor(last, cv2.COLOR_BGR2GRAY) if last.ndim == 3 else last)[1])
            grey = bool(abs(last[..., 0].astype(int) - last[..., 1].astype(int)).mean() < 2) if last.ndim == 3 else True
            tall = last.shape[0] > last.shape[1]
            mean = sum(tail) / len(tail)
            verdict = ("BLACK picture (no image)" if rng < 2 else "looks like INFRARED (grey, tall): USE THIS ONE" if grey and tall
                       else "colour camera (the Gaze Tracker uses this)" if not grey else "grey but not tall")
            say(f"  {idx}: {last.shape[1]}x{last.shape[0]} {info['fourcc']} {len(means)} frames, brightness {mean:.0f}/255 range {rng:.0f}, {'grey' if grey else 'colour'}: {verdict}")
        finally:
            cap.release()
    say("Use the number that says 'looks like INFRARED', e.g.  app.py --auto --mic N --ir-device THAT_NUMBER")


def run(a, stop, say=print):
    if getattr(a, "scan", False):
        scan(a, say)
        return
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
    import numpy as np
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

    last, t_status, frames, found, black_said, dim_said = 0.0, time.time(), 0, 0, False, False
    black_since, saved, last_save = None, 0, 0.0
    if a.save_frames:
        Path(a.save_frames).mkdir(parents=True, exist_ok=True)
    prev_gray, boxes, user, tracked, brightness, rng = None, [], None, [], 0.0, 0.0
    try:
        while not stop.is_set():
            ok, frame = cap.read()
            if not ok or frame is None:
                time.sleep(0.05)
                continue
            frames += 1
            now = time.time()
            if black_since is not None and now - black_since > 8:      # the stream stays black: open the camera again
                say("picture stayed black for 8 s: opening the camera again")
                cap.release()
                time.sleep(1.0)
                try:
                    cap, info = open_camera(idx, backend, a.ir_width, a.ir_height, "MJPG", 30)
                except RuntimeError as e:
                    say(f"ERROR reopening: {e}")
                    return
                for _ in range(20):
                    cap.read()
                black_since, prev_gray = None, None
                continue
            g = frame if frame.ndim == 2 else cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            gray = merge_flicker(prev_gray, g)          # lit and unlit infrared frames alternate: use the brighter of two consecutive frames
            prev_gray = g
            if now - last >= a.behind_every:
                last = now
                brightness = float(gray.mean())
                _, rng = enhance(gray)
                if rng < 2 and frames > 10:
                    black_since = black_since or now
                else:
                    black_since = None
                if rng < 2 and frames > 10 and not black_said:
                    black_said = True
                    say("WARNING: the picture is BLACK (no image at all, range %.1f). Wrong camera number, or the infrared stream is not delivering. "
                        "Run:  behind_monitor.py --scan   to find the right number." % rng)
                elif brightness < 8 and rng >= 2 and not dim_said:
                    dim_said = True
                    say(f"note: the infrared picture is very dark (brightness {brightness:.0f}/255) but has an image (range {rng:.0f}); it is brightened before the search.")
                boxes = detect(gray, cascade, a.min_face)
                faces = to_faces(boxes, gray.shape[1])
                user, tracked = handle(faces, now)
                found += len(boxes)
                if a.save_frames and saved < 15 and now - last_save >= 3:
                    last_save, saved = now, saved + 1
                    out = cv2.cvtColor(enhance(gray)[0], cv2.COLOR_GRAY2BGR)
                    for (x, y, w, h) in boxes:
                        cv2.rectangle(out, (x, y), (x + w, y + h), (0, 200, 0), 2)
                    cv2.imwrite(str(Path(a.save_frames) / f"ir_{saved:02d}_faces{len(boxes)}_b{brightness:.0f}_r{rng:.0f}.png"), np.hstack([cv2.cvtColor(g, cv2.COLOR_GRAY2BGR), out]))
            if a.preview:                                # drawn on every frame (with the boxes of the last search), so it does not blink
                shown = cv2.cvtColor(enhance(gray)[0], cv2.COLOR_GRAY2BGR)
                ordered = sorted(boxes, key=lambda b: b[2], reverse=True)
                for k, (x, y, w, h) in enumerate(ordered):
                    cv2.rectangle(shown, (x, y), (x + w, y + h), (0, 200, 0) if k == 0 else (0, 0, 255), 2)
                cv2.imshow("person behind (Esc closes)", shown)
                if cv2.waitKey(1) == 27:
                    stop.set()
            if now - t_status >= 5:
                t_status = now
                say(f"[{now - t0:4.0f}s] faces now: {len(boxes)} ({'you + ' + str(len(tracked)) + ' other' if user and tracked else 'you only' if user else 'nobody'}) "
                    f"| frames {frames} | brightness {brightness:.0f} range {rng:.0f} | plugin: {coach.sent} ok / {coach.failed} failed"
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
