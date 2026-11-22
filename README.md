# Work-Health Coach (hackathon-2026)

Camera/mic triggers -> haptics on the MX Master 4, pixel-art screens on the MX Keypad, cute animals on screen.

| Read this | If you are |
|---|---|
| [docs/HANDOFF.md](docs/HANDOFF.md) + `CLAUDE.md` | **new on the project or continuing in a new Claude session: start here** |
| [docs/TRIGGER_CONTRACT.md](docs/TRIGGER_CONTRACT.md) | on the **camera / mic** side: what to send and when |
| [docs/DEMO.md](docs/DEMO.md) | **presenting to the Logitech team: the 10-minute running order, checklist and fallbacks** (presenter view: `http://127.0.0.1:8787/#/demo`) |
| [docs/PITCH_1MIN.md](docs/PITCH_1MIN.md) | **the one-minute pitch for the jury** (one live moment, one sentence for the rest, one ask) |
| [docs/WINDOWS_DEMO.md](docs/WINDOWS_DEMO.md) | **running the demo on Windows: one window, one command** (owl + snail) |
| [docs/CAMERA_TEST.md](docs/CAMERA_TEST.md) | one-time Windows setup: camera network, tools |
| [docs/SETTINGS_UI.md](docs/SETTINGS_UI.md) | on the **settings page, feature keys and Pomodoro** (served by the plugin at http://127.0.0.1:8787/) |
| [docs/POLICY.md](docs/POLICY.md), [docs/KEYPAD_SCREEN.md](docs/KEYPAD_SCREEN.md), [docs/OVERLAY.md](docs/OVERLAY.md) | on the **feedback** side |
| [docs/DESIGN.md](docs/DESIGN.md), [docs/RESOURCES.md](docs/RESOURCES.md) | decisions and Logitech links |

Layout: `plugin/` (C# Logi plugin: haptics, Keypad, policy, local trigger server), `brain/` (Python: camera probes, trigger client), `overlay/` (on-screen animals), `assets/` + `scripts/` (sprites, test tools).
