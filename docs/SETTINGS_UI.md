# Settings page, feature keys and Pomodoro (served by the plugin)

State: built and tested **without devices** (60 harness checks, real Keypad drawing code). Not yet tried on a real MX Master 4 / MX Keypad: see "Not connected yet" at the end.

## What it is
The plugin now serves its own settings page: **http://127.0.0.1:8787/** (embedded in the DLL, same server as the trigger API).
Per feature: on/off, feedback types (audio, screen, mouse haptics, keypad colour), per-feature settings, cooldown, "reset to recommended".
Plus a locked **Screen brightness** concept card (the camera gives no brightness data), profiles (Everyday, Presenting = silent, Deep focus, Video meeting) and a master switch.
Source: `plugin/WorkHealthCoachPlugin/src/Web/` (`index.html`, `assets/icons`, `assets/sounds`). Opened as a plain file it still works as a preview.

## How settings reach real triggers
The page stores one JSON document (`ui-state.json`, next to `settings.json`) through `GET/POST /ui-state`.
`Policy/UiSettings.cs` maps it onto the existing `NotificationPolicy` rules whenever it changes:

| Page feature | Triggers | Mapped fields |
|---|---|---|
| Speech speed | `pace_too_fast` | enabled, cooldown, channels (screen=`Overlay`, keypad=`Visual`, haptics=`Haptic`, audio=`Audio`), haptic pattern + repeat, Keypad colour |
| Speech volume | `speak_up` (team's detector + duck), `speak_down` (new) | same |
| Eye blinking | `blink_low` | same |
| Pomodoro | `break_due` (team's Python timer sends it; the plugin timer can too), `break_over` (new) | same + the timer settings below |
| Volume when far | `volume_far` (new) | same (no feedback by default); the plugin itself changes the volume, see below |
| Person behind | `person_behind` | same |

Also mapped: master switch (`PolicySettings.Master`), profile and its muted channels (`Muted`). `MinSeverity`, `HapticAfterSec` (escalation), budget, presets, snooze are unchanged and still apply.
**Defaults changed to the team table** the first time the page opens: pace 30 s, blink 20 min, behind 60 s, speak up/down 30 s (before: 90 s / 5 min / 20 s). Reset by deleting `ui-state.json` and `settings.json`.
Screen text, style, GIF, sound and audio are stored and exposed (`ScreenText`, `ScreenStyle`, `Sound`, `Audio` on each rule) but nothing in the plugin plays them yet.

## Haptics (MX Master 4)
The page offers four patterns per feature. Events and waveforms (`package/events/*.yaml`): Tap `hapticTap` = `sharp_state_change`, Bump `hapticBump` = `damp_collision`, Alert `hapticAlert` = `angry_alert`, Jingle `hapticJingle` = `jingle`. Repeat 1-3 times; strength Off/Light/Normal/Strong still applies. No choice = the trigger's built-in pattern. New triggers have built-in patterns too.

## MX Keypad
- **Feature keys:** Options+ -> All Actions -> search "Desk Coach": one action per feature, plus Master switch and Presenting mode (`Actions/FeatureToggleCommand.cs`). Pixel icon on, dimmed off (icons in `Resources/feat_*.png`, from `scripts`-style 16x16 art). The Pomodoro key shows the live countdown wherever it sits.
- Keys change the same document as the page, so page, keys and policy stay in sync (the page polls every 2 s).
- **New reminders** take over the whole Keypad like the eye and snail do: SPEAK UP, LOWER VOICE, BREAK TIME (green), BACK TO FOCUS, VOLUME UP. Colour comes from the page's Keypad colour (image upload is stored but not drawn yet).
- **Pomodoro timer** in the Coach screen folder (same layout as the idle screen): top = FOCUS 1 OF 4 + AT DESK / AWAY / PAUSED / REST, middle = huge MM:SS, bottom = reset / play-pause / skip. Limit from the handoff: a folder cannot be opened by code, so the user presses the Coach screen key once; the Pomodoro feature key shows the countdown without opening anything.

## Pomodoro (`Session/PomodoroState.cs`)
Focus 25 / short 5 / long 15, long break after 4 (all settable). With camera presence on, the focus clock only runs while at the desk and an absence of 5 min or more resets it; breaks run on the wall clock. End of focus = `break_due`, end of break = `break_over` (through the policy). Feature on = timer on.
API: `GET /pomodoro`, `POST /pomodoro/start|stop|toggle|skip|reset|demo-away`, presence: `GET|POST /signal/presence/true|false` or `POST /presence {"atDesk":true}`.

## API added
`GET /` page, `GET/POST /ui-state`, `GET /pomodoro`, `POST /pomodoro/<action>`, `POST /presence`, `/signal/presence/<bool>`, and `/trigger` accepts `"only":"haptic|keypad|screen|audio"` together with `"force":true` (test one channel). New trigger names: `speak_down`, `break_over`, `volume_far` (`speak_up`, `speak_ok` and `break_due` were added by the team's Python detectors at the same time; their patterns and rules were kept).

## Tests
```
cd plugin/WorkHealthCoachPlugin/tests/Harness && ./run.sh       # 60 checks, throw-away settings folder, no Options+ needed
./bin/Release/net10.0/Harness --render <folder>                  # draws the Keypad screens to PNG
dotnet build -c Release -p:LinkPlugin=false                      # compile in src/ without linking into Options+
```
`WORKHEALTH_DATA_DIR` overrides the settings folder and `WORKHEALTH_PORT` the server port (default 8787). The harness uses its own port (18787) and refuses to run on 8787, so it can never touch the plugin running inside Options+.

## Volume when far (no camera needed to build or test)
`Session/FarVolume.cs` + `Audio/SystemAudio.cs`. The plugin raises the computer's volume when you are far from the screen, and mutes it when you have left.
- **Far** = the face is clearly smaller than at your normal seat (ratio <= 1 - shrink %, default 30 %) **or** you are out of the frame, for at least "Act after" (2 s).
- **Far ->** volume +20 % points (gradually, 5 points a second), never above "Never louder than" (default 90 %). One `volume_far` trigger fires (no feedback by default).
- **Near again ->** the previous volume comes back. If you turned the volume by hand in between, it leaves your setting alone and will not raise it again until you have been near.
- **Out of the frame for 120 s ->** mute (default) or press Play/Pause; coming back undoes it. Play/Pause is a toggle and would START playback if nothing was playing, so mute is the default. If you had it muted already it stays muted.
- Feature off, master off or plugin unload: sound and volume are given back.
- **Inputs (any detector, or the page's simulator):** `POST /distance {"ratio":0.6}` (face size vs normal; stale after 5 s, so keep sending) or `{"face":0.11}` after `POST /distance/calibrate` ("this is my normal distance", optional `{"face":0.18}`), plus presence `/signal/presence/true|false` (shared with Pomodoro). `GET /distance` returns the state and the real system volume.
- **Try it:** Customize -> "Try it without the camera" (slider and "I leave the frame"). It really changes your volume; leaving the page puts presence back.
- **Systems:** macOS via `osascript` (tested: read and set-same-value on a real Mac). Windows via Core Audio (`IAudioEndpointVolume`): written from the documented interface, compiles, **not run on Windows yet**. Other systems do nothing.
- The camera side only needs to send the face width (or the ratio) and "face seen / not seen". Nothing here opens the camera.

## How the Python detectors fit (integration, 2026-11-22)
- **One Pomodoro clock: the plugin's.** `pomodoro_monitor.py` now only sends presence (`/signal/presence/true|false`); `--own-timer` is the old
  standalone mode (do not combine). See `docs/POMODORO.md`.
- **Page switch = policy switch.** Pomodoro is OFF on the page by default: the clock does not run and `break_due` is dropped ("disabled" in the log) until
  the user switches it on (page or Keypad key). Same idea for every feature: off on the page = its triggers are off.
- **Mouse on the first `break_due`.** The plugin timer sends `break_due` once per phase, so `HapticAfterSec` for `break_due` / `break_over` is 0 (also forced in `UiSettings.Apply`,
  because an existing `settings.json` may still carry the old 5 s escalation).
- **Duck / snail / owl** are driven by `speak_up` / `pace_too_fast` / `blink_low` through the page's "screen" channel; `speak_ok`, `pace_ok`, `blink_detected` end them.
  The **Presenting** profile mutes the screen channel, so the animals do not show in that profile (harness check "presenting mutes the on-screen channel").
- **Thresholds on the page are not read by the Python detectors yet** (speed wpm, volume dB, blink rate, "second face for N s"). Only the plugin side is applied
  (on/off, cooldown, channels, haptic pattern, Pomodoro timings). The detectors keep their own defaults (`--fast-rate`, `--quiet-db`, `--no-blink-after`).
- **Cooldown defaults** on first page open follow the team table (pace 30 s, blink 20 min, behind 60 s, speak up 30 s) and replace the plugin's 90 s / 5 min / 20 s / 90 s.
  Blink: 20 min cooldown and no mouse buzz by default, so a demo of the blink reminder needs `--force` or a lower cooldown / the haptic channel switched on.

## Not connected yet (next phase)
1. **Devices:** `dotnet build -c Debug` in `src/` (links into Options+), drag the "Desk Coach" keys and "Coach screen" onto the Keypad, press them, test the four haptic patterns on the MX Master 4 from the page. Nothing here has run on real hardware yet.
2. **Detectors -> triggers:** `speak_up`/`speak_down` (mic), `person_behind` (second face), `volume_far` (face size), camera presence `/signal/presence/...` for Pomodoro. Only `pace_too_fast` and `blink_low` have detectors today.
3. **Screen and audio feedback:** the overlay (`/overlay`) and a sound player must read the new rule fields.
4. Team decision: blink logic (their "no blink for 8 s" vs the table's rolling 5 min rate) and pace thresholds.
