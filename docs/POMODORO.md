# Pomodoro with the Logi Gaze Tracker (one clock, owned by the plugin)

**The plugin owns the clock** (`PomodoroState`, settings page, Keypad timer, `break_due` / `break_over`). The Python monitor
(`brain/src/brain/monitors/pomodoro_monitor.py`) only tells it where you are: `/signal/presence/true|false`.

| Situation seen by the Gaze Tracker | Sent to the plugin | Clock |
|---|---|---|
| face seen, eyes on the screen (5 % margin) | at the desk | runs |
| no face < 30 s, or eyes off-screen < 60 s | at the desk (short glance / short trip) | runs |
| no face >= 30 s, or eyes off-screen >= 60 s | away | **stops**, continues when you are back |
| away for the page's "reset after" minutes (default 5) | away | plugin resets the timer (a break) |
| Gaze Tracker app not running | nothing | the plugin keeps its last state |

Looking down at a phone usually makes the Gaze Tracker lose your face completely, so it counts as "no face" (pause after 30 s).

## Run it
1. Install **Gaze Tracker** (Windows installer: https://github.com/Logitech/hackathons/releases/tag/logi-gaze-tracker), tray icon -> Controls -> Setup
   (camera, 1920x1080, screen size in mm, camera position), **Apply rig**, **Start calibration** (9 points).
2. Rebuild the plugin (`dotnet build -c Debug` in `plugin/WorkHealthCoachPlugin/src`), restart Options+.
3. **Switch Pomodoro ON**: settings page http://127.0.0.1:8787/ (Pomodoro card) or the "Pomodoro" key on the Keypad. Until it is on, no clock runs and
   `break_due` is dropped by the policy (the monitor prints a hint).
4. `cd brain && uv run python src/brain/app.py --no-blink --no-pace --pomodoro` (chip in the top-right corner: green WORK, grey PAUSED, blue BREAK).
   Without the launcher: `uv run python src/brain/monitors/pomodoro_monitor.py` (no chip). `--simulate` = scripted fake person, no hardware, 80 s.

## Old mode
`--own-timer`: the Python program keeps its own clock (`--work-min`, `--break-min`) and sends `break_due` every 2 s until you take a break.
Only use it with the plugin's Pomodoro **switched off** in the plugin, otherwise you get two clocks and double reminders.

## Verified
- Plugin clock + simulated presence together (harness `--serve` + `pomodoro_monitor.py --simulate`): the clock stops ~5 s after "away", resumes on return.
- Real Windows run of the EARLIER version (Python-owned clock): presence (`face`/`gaze` become `null` when you leave), pause / resume, the chip, the break reminder.
- Policy: the mouse buzzes on the FIRST `break_due` (the plugin timer sends it once per phase; `HapticAfterSec = 0`).

## Unverified
- The new presence-only path on the real Windows machine (plugin clock + real Gaze Tracker): only run in simulation so far.
- The Gaze Tracker next to the LogiInsight camera AI running at the same time.
- Thresholds (30 s / 60 s) were chosen from one look-away test.
