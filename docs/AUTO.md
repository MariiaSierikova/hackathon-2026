# One start, then the Keypad decides (`app.py --auto`)

Start the launcher once; it WATCHES the plugin's switches (Keypad keys or settings page, polled every 2 s) and starts / stops the monitors itself:

| Switch | What runs | Needs |
|---|---|---|
| Pomodoro on **or** Volume when far on | gaze monitor (Gaze Tracker -> desk presence, pause / resume, face size) and the on-screen timer chip (it shows only while the Pomodoro is on) | Gaze Tracker app running + calibrated |
| Speech speed **or** Speech volume on | microphone monitor (snail, duck) | `--mic N` |
| Person behind on | infrared-camera monitor | `--ir-device N` |
| Master switch off | nothing | |
| Eye blinking | **not managed**: the camera's AI overheats after ~10 min, so blink stays a manual `app.py --device N` start | |

```
cd brain
uv run python src/brain/app.py --auto --mic 11 --ir-device 2
```
(`--list` shows the numbers; leave `--mic` / `--ir-device` out and those features simply are not started.) Press the Pomodoro key on the Keypad: the timer chip appears, presence flows, the
clock pauses when you leave. Press it again: the gaze monitor stops. A monitor that crashes is retried after 15 s. If the plugin does not answer (Options+ restarting), nothing is changed.
Ctrl+C stops everything. Tests: `brain/tests/test_supervisor.py`.

# The Alert light key
The ninth Desk Coach key is a switch for ALL alerts: **ALERTS ON** (green) / **ALERTS OFF** (red). Off = the policy drops every alert (mouse, Keypad, screen, sound), also person behind and time
warnings; the features keep running (Pomodoro clock, volume when far). Stored in `settings.json` (`alertsOn`), so it survives a restart. Only forced test triggers (`--force`) still show.
While an alert is showing, any key press dismisses it (it does not toggle), so to switch alerts off during an alert press twice.
