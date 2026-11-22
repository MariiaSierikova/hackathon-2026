# speak_up: yellow duck when you are too quiet

Source: teammate's `logiinsight_api/newscripts/speech_loudness_flagger.py` (5 s window, -25 dBFS), reimplemented as `brain/src/brain/audio/loudness.py`
and run inside `pace_monitor.py` / `app.py` (same microphone read as the pace code).

**Change vs. the teammate's script:** the level is the RMS of the frames where someone is *speaking* only. The original averaged the whole 5 s window
including silence, so every pause would have counted as "too quiet" (and it showed no result when not speaking). Its file also mixes tabs and spaces
(the loudness block sits inside `update()` with tab indentation), so it would not run as pushed; its GUI was not reused.

| | |
|---|---|
| quiet | speech level < `--quiet-db` (default -25 dBFS) with >= 1 s of speech in the last 5 s |
| fine again | level > `--ok-db` (default -22 dBFS), or no speech |
| output | `speak_up` -> duck + mouse tap (policy: severity 0.5, cooldown 90 s, mouse after 5 s, max 4 / 10 min); `speak_ok` -> duck leaves |

Test: `uv run python src/brain/monitors/pace_monitor.py --simulate` (quiet 8-22 s, fast 26-42 s) or `overlay/overlay.py --demo duck`; `python scripts/trigger.py speak_up --force`.
Rebuild the plugin after pulling (new trigger + `speak_ok`): `dotnet build -c Debug`.

**Calibrate:** -25 dBFS is the teammate's number and is untested on the real microphone; the printed `level ... dBFS` line every 3 s shows your normal speaking level.
A noisy room raises the "speech" floor and reads as quieter than you are. The Keypad shows only its generic alert for `speak_up` (no duck drawn there yet).
