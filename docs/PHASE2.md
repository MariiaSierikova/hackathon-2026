# Phase 2a - Trigger server (no camera needed)

The plugin now listens on `http://127.0.0.1:8787` (this computer only).

| Call | What it does |
|---|---|
| `GET /health` | `{"ok":true}` when the plugin is running |
| `GET /triggers` | lists trigger names |
| `POST /trigger` | `{"trigger":"pace_too_fast","severity":0.8}` -> fires the haptic pattern (and later Keypad + overlay) |

Anything can send triggers: the real camera code, a test script, or a teammate's program.

## Try it (Terminal, in the project folder)
```bash
python3 scripts/trigger.py --list
python3 scripts/trigger.py pace_too_fast
python3 scripts/trigger.py blink_low
```
Expected: the same buzz patterns as the Keypad test keys.

## Code map
- `src/Bridge/LocalServer.cs` - the HTTP listener.
- `src/Bridge/TriggerBus.cs` - one in-process channel; haptics, Keypad widgets and policy subscribe to it.
- `src/WorkHealthCoachPlugin.cs` - starts the server, connects the bus to `HapticPlayer`.
- `scripts/trigger.py` - command-line trigger sender (Python standard library only).
