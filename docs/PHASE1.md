# Phase 1 - "Hello buzz": press a Keypad key, the MX Master 4 vibrates

## What Claude already did
- Generated the plugin `plugin/WorkHealthCoachPlugin/` with `logiplugintool generate`.
- `src/package/metadata/LoupedeckPackage.yaml`: added the `HasHapticMapping` capability (turns haptics on for the plugin).
- `src/package/events/DefaultEventSource.yaml`: defines 5 haptic "pulses" (soft, calm, sharp, knock, done).
- `src/package/events/extra/eventMapping.yaml`: maps each pulse to one of Logitech's 15 waveforms (e.g. calm -> `wave`, soft -> `subtle_collision`).
- `src/Haptics/HapticPlayer.cs`: plays a **pattern** = several pulses in a row (haptics have no duration setting). Example: `pace_too_fast` = 4 calm pulses 450 ms apart.
- `src/Actions/HapticTestCommand.cs`: one Options+ action per pattern, so you can test with no camera.
- Built it (`dotnet build`). The build wrote a link file so the Logi Plugin Service loads the plugin straight from our folder.
- Verified in the service log: `Plugin 'WorkHealthCoach' ... loaded` and `Event source added ... capabilities 'HasHapticMapping'`.

## What you do (in Logi Options+)
1. Open **Logi Options+** and choose your **MX Keypad**.
2. Open its customization view, then **All Actions** (search "Buzz").
3. If **WorkHealthCoach** isn't listed under *Installed Plugins*: Options+ **Settings -> Restart Logi Plugin Service**, then look again.
4. Drag **Buzz: time_warning** onto a Keypad key, and **Buzz: pace_too_fast** onto another.
5. Press the keys. Expected: quick double tap (time_warning), then a slower long wave (pace_too_fast) on the MX Master 4.
6. No buzz? Check, in order: the mouse is connected to Options+ and its **haptic feedback** setting is on; the mouse battery isn't low; then tell Claude exactly what you see. Logs: `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/WorkHealthCoach.log`.

## Known cosmetic detail
The log shows 10 events (the 5 pulses are registered both in YAML and in code, as Logitech's own tutorial does). Harmless; we clean it up after the buzz is confirmed.

## Next (Phase 2)
Plugin starts a local server on `localhost:8787` so `curl` / the trigger tool can fire these patterns, and Keypad widgets (timer, pace, blinking eye) get drawn.
