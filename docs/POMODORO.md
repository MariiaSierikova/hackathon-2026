# Pomodoro (break_due) with the Logi Gaze Tracker

Work clock runs only while you are at the computer and looking at the screen. 25 min of real work -> `break_due`
(Soft x3 buzz after 5 s if it persists, through the notification policy). Taking a real break (away 5 min) restarts the clock and sends `lookaway_done`.

| Situation | Clock |
|---|---|
| face seen, eyes on screen (5 % margin) | runs |
| no face < 30 s / eyes off-screen < 60 s | still runs (short glance / short trip) |
| no face >= 30 s or eyes off-screen >= 60 s | **paused**, continues by itself |
| away >= 5 min | restarts from 0 (a break) |
| Gaze Tracker app not running | clock does not run (data gap never counts) |

## Run
1. Install **Gaze Tracker** (Windows installer: https://github.com/Logitech/hackathons/releases/tag/logi-gaze-tracker), tray icon -> Controls -> Setup
   (camera, 1920x1080, measure screen mm + camera position), **Apply rig**, then **Start calibration** (9 points, click/Space on each).
2. `cd brain && uv sync` (adds `websockets`)
3. `uv run python src/brain/monitors/pomodoro_monitor.py --simulate` (no hardware, 80 s) then
   `uv run python src/brain/monitors/pomodoro_monitor.py --work-min 1 --force` (real, 1 min for a quick test)
4. Rebuild the plugin (`dotnet build -c Debug`): `break_due` is a new trigger and the old plugin answers 400 to it. Check `/health` build time.

## Unverified (test on the real machine)
- Does the Gaze Tracker run next to the LogiInsight camera (one camera, two programs)? If not, the Gaze Tracker needs another webcam.
- `gaze` is documented as `null` without a face OR without calibration; we count presence from `face.bounding_box_norm` too, but that field's behaviour with no face is not documented. Watch the printed "face / no face" in the log.
- Thresholds (30 s / 60 s / 5 min) are defaults from HANDOFF, tune with the flags.
- Not in `app.py` yet; run it as its own process. Keypad has no special break screen yet (generic alert only).
