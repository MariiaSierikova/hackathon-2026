# Logitech Hackathon resources: what each link is and why we use it

Source: https://logitech.github.io/hackathons/ (a resource index only: no dates, rules, judging criteria or prizes are published there).
Repo behind it: https://github.com/Logitech/hackathons (branch `master`).

Legend: **USE** = core to our project, **MAYBE** = optional/fallback, **SKIP** = not relevant.

## PC peripherals

| # | Link | What it does | Why / verdict |
|---|---|---|---|
| 1 | [LogiInsight quick start](https://logitech.github.io/hackathons/logiinsight/) | Install + connect guide for the hackathon webcam (USB video, USB mic, USB-network link to the camera AI) | **USE** - our sensor |
| 2 | [LogiInsight API reference](https://logitech.github.io/hackathons/logiinsight/API) | Full reference: RGB/IR streams, `LogiWebcamClient`, AI JSON (face box, 478-pt mesh incl. iris 468-477), audio (2 mics) | **USE** |
| 3 | [SDK zip v1.5.2](https://github.com/Logitech/hackathons/releases/download/logiinsight-sdk-v1.5.2/logiinsight_api_sdk_v1.5.2.zip) | `logiwebcam.py`, `camera_utils.py`, `audio_utils.py`, examples, `check_connection.py` (MIT) | **USE** |
| 4 | [Gaze Tracker quick start](https://logitech.github.io/hackathons/logi-gaze-tracker/setup/) | Install/calibrate a tray app that estimates screen gaze from any Logitech webcam | **MAYBE** - "did you look away from the screen" for 20-20-20 |
| 5 | [Gaze Tracker sample code](https://logitech.github.io/hackathons/logi-gaze-tracker/sample-code/) | WebSocket protocol `ws://127.0.0.1:8964`, `gaze_logger.py`, `gaze-viewer.html` | **MAYBE** |
| 6 | [Gaze Tracker app (releases)](https://github.com/Logitech/hackathons/releases/tag/logi-gaze-tracker) | macOS arm64/Intel + Windows installers (unsigned) | **MAYBE** |
| 7 | [Hands Tracker quick start](https://logitech.github.io/hackathons/logi-hands-tracker/) | Thumb/index pinch tracking over `ws://127.0.0.1:8765`; needs the LogiInsight CPD camera | **SKIP** (stretch: air-gesture slide control) |
| 8 | Hands Tracker sample code (same page) | `hand-viewer.html`, `hand_tracker_log.py` | **SKIP** |
| 9 | [Hands Tracker app (releases)](https://github.com/Logitech/hackathons/releases/tag/hands-tracker) | Installers | **SKIP** |

## Actions SDK (our output side: mouse haptics + keypad screens)

| # | Link | What it does | Why / verdict |
|---|---|---|---|
| 10 | [Haptics tutorial](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-tutorial/) | Generate a plugin, add `HasHapticMapping`, event + mapping YAML, `RaiseEvent` (page link on hackathon site 404s; this is the real URL) | **USE** |
| 11 | [Actions SDK docs](https://logitech.github.io/actions-sdk-docs/) | C# plugin docs (Node.js SDK is end-of-life) | **USE** |
| 12 | [Demo plugin template repo](https://github.com/Logitech/actions-sdk) | DemoPlugin, **HapticsPlugin**, **SnakePlugin** (draws on the 3x3 keypad) | **USE** - copy patterns |
| 13 | [Agent docs zip](https://logitech.github.io/hackathons/res/LogiActionSDK_agent_doc.zip) | Markdown docs for coding agents; includes "web apps as actions" (plugin may host a localhost HttpListener) | **USE** |
| 14 | [Haptics overview](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-overview/) | Haptics need Options+ >= 1.95 and an MX Master 4 | **USE** |

Haptic facts: 15 named waveforms (`sharp_state_change`, `damp_state_change`, `sharp_collision`, `damp_collision`, `subtle_collision`, `happy_alert`, `angry_alert`, `completed`, `square`, `wave`, `firework`, `mad`, `knock`, `jingle`, `ringing`). No duration/repeat setting, so a "long calm" buzz = several pulses in a row.

## WebHID community libraries (Chrome only; fallback / prototyping)

| # | Link | What it does | Verdict |
|---|---|---|---|
| 15 | [mx-creative-console-webhid](https://github.com/mario-gutierrez/mx-creative-console-webhid) | Keypad + Dialpad over WebHID: key events, brightness, upload images to keys (118x118, <=10 fps) | **MAYBE** fallback |
| 16 | [mx-cc-script (Keypad Script Lab)](https://github.com/mario-gutierrez/mx-cc-script) | Web script engine for the Keypad + `agents.md` developer guide | **MAYBE** |
| 17 | [mx-master-4-webhid](https://github.com/mario-gutierrez/mx-master-4-webhid) | Raw haptic effect ids 0-14 for MX Master 4 from a browser (experimental, unofficial) | **MAYBE** fallback |
| 18 | [litra CLI](https://github.com/timrogers/litra) | Control Litra Glow/Beam lights | **MAYBE** (only if we own a Litra) |

## XR / Gaming (not relevant)

| # | Link | Verdict |
|---|---|---|
| 19-23 | MX Ink Unity docs + SDK site (3 links), Apple WWDC25 session, Logitech Muse RealityKit gist | **SKIP** |
| 24-30 | Partner Developer Lab + LED Illumination, G-Key Macro, ARX Control, Game panel (LCD), Steering Wheel SDK zips | **SKIP** |

## Other resources

| # | Link | What it does | Verdict |
|---|---|---|---|
| 31-39 | Microsoft / Apple / freedesktop / PyPI docs on capturing + injecting input events | OS key hooks and synthetic key presses | **MAYBE** - slide control (send arrow keys) |
| 40 | [InputLogger.zip](https://logitech.github.io/hackathons/res/InputLogger.zip) | Windows C# input-capture example | **SKIP** (we are on macOS) |
| 41-42 | Discord: [MX collective](https://discord.gg/eYGbhJdSu8), [Actions SDK + MX devices](https://discord.gg/8TWwHWuymm) | Community help | **USE** when stuck |
| 43-47 | Past events: Lauzhack 2023/2019/2018/2017, HackZurich 2020 | Old projects | inspiration only |
| - | View on GitHub, Logitech footer logo | navigation | - |

## Warnings found while reading

- `docs/options-plus/*` (features.cfg, `logi_plugin_sdk.pdf`, weather plugin zips) is the **legacy 2023 plugin system**. Do not follow it. Current system = Actions SDK + Logi Plugin Service.
- LogiInsight AI reports **at most one face** and must not run **~10 minutes continuously** (overheats). Design around both.
- Hackathon page links `/csharp/haptics-tutorial/` and `/csharp/Haptics-Overview/` are 404; the real paths are under `/csharp/haptics/`.
- Other repo content (`devmon/`, `cv/`, `fastPose`) is older Lauzhack tooling, not needed.
