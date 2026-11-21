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

# Phase 2b - Keypad widgets

Seven actions appear in Options+ (search "Coach"). Drag any onto Keypad keys; **leave out the ones you don't want**: that is the user-customisable layout.

| Action | Shows | Press does |
|---|---|---|
| Session timer | time left (green -> amber -> red) | start / pause |
| Slide timer | slide number + time on slide | count next slide |
| Pace meter | words/min, turns red "SLOW DOWN" after `pace_too_fast` | - |
| Blink eye | eye; blinks twice on `blink_low`, "BLINK!" hint | - |
| Alert | latest alert text/colour for 12 s, then "all calm" | - |
| Next slide / Previous slide / Reset session (one action, 3 variants) | - | sends Right / Left arrow key to the presentation app |
| Buzz: ... (Phase 1) | - | haptic tests |

Automatic: when the session timer reaches `WarnMinutes` (default 1) before the end, the plugin fires `time_warning` itself (quick double haptic).

## Try it
```bash
python3 scripts/trigger.py --wpm 172      # pace meter number
python3 scripts/trigger.py pace_too_fast  # buzz + pace tile turns red + alert tile
python3 scripts/trigger.py blink_low      # eye blinks + soft double tap
python3 scripts/trigger.py person_behind
```
Press the Session timer key to start it (default 20 min; changeable later in settings).
