# Camera feeds for "Volume when far" and "Person behind"

The plugin holds the decision logic (see `docs/SETTINGS_UI.md`); these programs feed it real camera data.

## Volume when far (Gaze Tracker, colour camera)
`brain/src/brain/monitors/pomodoro_monitor.py` (also started by `app.py --pomodoro`) now sends, besides presence:
- **face size** (`face.bounding_box_norm.width` of the Gaze Tracker snapshot) -> `POST /distance {"face": w}`, twice a second while a face is seen;
- **face gone** for `--far-after` seconds (default 3) -> `POST /distance {"ratio": 0.3}` every second (the plugin treats that as far, then waits its own "Act after", 2 s).
  Presence itself (`/signal/presence`) still has the 30 s grace for the Pomodoro, so the mute-after-leaving timer starts after that grace.
- The plugin needs a **normal distance**: if it has none, the monitor takes your current face size after 3 steady seconds. Set it on purpose, sitting normally:
  `uv run python src/brain/monitors/pomodoro_monitor.py --calibrate-distance`
- `--no-distance` turns the feed off. The feature itself must be ON on the settings page ("Volume when far").

## Person behind (infrared camera, `behind_monitor.py`)
The LogiInsight camera's AI reports ONE face and the Gaze Tracker only one, so this looks for faces itself on the camera's **infrared** stream
(`LVNext CPD Camera`: grey, portrait, works in the dark, a separate device from the colour stream the Gaze Tracker uses). OpenCV's frontal face detector
(`brain/vendor/haar/haarcascade_frontalface_alt2.xml`, from the Logitech repo): the biggest face is YOU, every other face goes to `POST /behind`
with a stable id, x position and size; the plugin decides (walk past = ignored, comes to you / stays = alert).
- `uv run python src/brain/app.py --list` shows the camera numbers (the grey and tall one is infrared); then
  `uv run python src/brain/monitors/behind_monitor.py --ir-device N --preview` (a window with boxes: green = you, red = others) or `app.py --behind --ir-device N ...`.
- `--simulate` = a made-up visitor, `--image file.png` = look at one picture.
- **Range:** faces smaller than ~36-40 px (of 512) are not found: a person about **1 to 1.5 m** from the camera. Further away is invisible to this detector.
  `--min-face 24` (default) can be lowered a little for more range and more false alarms.
- **OpenCV 4 is required** (`opencv-python>=4.8,<5` in `pyproject.toml`): OpenCV 5 removed `CascadeClassifier`. After pulling run `uv sync`.
- Frontal faces only: someone seen from the side or from behind (hair only) is not detected.

## Verified / not verified
- Verified here (no camera): the detector finds a second face in a picture; the simulated visitor and the simulated far-away person reach the real plugin logic and produce
  one `person_behind` alert (mouse, Keypad, screen) and `ratio` 0.3 / back to 1.
- NOT verified on a real camera: the infrared picture, detection on real infrared faces, the camera number, and the Gaze Tracker face size as a distance measure.
