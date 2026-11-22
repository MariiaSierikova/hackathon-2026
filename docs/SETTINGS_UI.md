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
  Each key is redrawn **by name** (`ActionImageChanged(key)`: the Logitech docs say the no-argument call is for actions without parameters), so a press or a page switch changes only that key. The Pomodoro key alone is redrawn every second for the countdown. Master off greys every feature key on purpose (everything is paused); Presenting greys the features that profile switches off.
  Debugging: every key press (`Desk Coach key pressed: 'speed'`) and every image request (`Desk Coach key image: 'speed' ON 80px`, verbose) is in the plugin log.
- **Alerts on the widget keys (no Coach screen folder needed):** while an alert is active (person behind, break time, speak up, blink, slow down, time), EVERY "Desk Coach" key
  (the feature toggles and the new **Alert light** action) flashes the same alert and goes back to its icon afterwards. A key press during an alert dismisses it (it does not
  flip the feature). The keys do not know where they sit, so each draws the whole short message instead of one tile of a 3x3 picture. The Coach screen folder still shows the big picture.
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

## Person behind: only when someone comes TO you (no camera needed to build or test)
`Session/PersonBehind.cs` (decision), `Session/BehindResponse.cs` (what you can do), `Actions/PersonBehindCommand.cs` (Actions Ring / Keypad actions).
Headphones on: you cannot hear a colleague arrive, so the alert is the mouse buzz (pattern "Alert"), the Keypad "SOMEONE BEHIND YOU" screen and the screen notification (all three on by default; the team table had screen only).
- **Input (any detector, or the page's simulator):** `POST /behind {"id":2,"x":0.62,"size":0.07}` a few times a second for every second face (`x` 0 = left .. 1 = right of the picture, `size` = face width / picture width, `id` optional but needed for two people at once); `POST /behind/clear` when nobody is there. Nothing here opens the camera.
- **Walking past vs coming:** each person is followed for a few seconds. Sideways speed is measured in face widths per second, so it does not depend on distance (a person walking by moves 5+ face widths a second; someone standing moves < 1).
  - **Walking past** (> 2.5 face widths/s sideways): ignored.
  - **Coming** (face grows 25 % or more in about 3 s without crossing sideways): alert, severity 0.9.
  - **Stays** (seen for 3 s or more, hardly moving sideways): alert, severity 0.7.
  - **Too far** (face smaller than a person at "Only count people within", default 3 m; face width is about 0.08 of the picture at 1 m on a 90 degree lens): ignored.
  - Everyone needs to be seen for "A second face for at least" (2 s) first. "Ignore people who just walk past" off = any second face for that long alerts (severity 0.6). One alert per visit; the policy's 60 s cooldown still applies.
- **What happens by itself** ("When someone comes to me"): *Only notify me* (default), *Hide windows (my shortcut)* (presses the shortcut you recorded, optional "bring back" shortcut and "bring back after N s"), or *Lock the screen* (macOS Ctrl+Cmd+Q, Windows Win+L). Windows: record ⌘ for the Windows key; Win+D hides and brings back.
- **Mouse ring and keypad:** in Options+ open the MX Master 4 -> **Actions Ring** and drag the actions under **Desk Coach: person behind** onto ring buttons (also work on Keypad keys): Hide my screen, Show my screen again, Pause / resume music, Talk mode (lowers the system volume to "Talk mode volume", default 30 %, never raises it, restores on the second press unless you changed it), It is fine (dismisses and ignores visitors for a minute), Snooze 15 min.
  The same actions: `POST /behind/do/hide|back|lock|music|talk|ok|snooze`. `GET /behind` shows who is tracked and why (for tuning).
- **Not verified:** assigning these to the Actions Ring inside Options+ (needs the UI); the shortcuts on a real Mac or Windows PC (use the page's Test buttons: "Hide my screen" really presses your shortcut); the thresholds on real camera data (2.5 / 1.0 face widths/s, +25 %, 0.08 at 1 m are reasoned defaults, not measured); a second-face detector with person ids (the LogiInsight camera reports one face only).
- Ideas not built: duck the music automatically when someone approaches; a quiet "someone is waiting" reminder if you dismissed a visitor who stays.

## How the Python detectors fit (integration, 2026-11-22)
- **One Pomodoro clock: the plugin's.** `pomodoro_monitor.py` now only sends presence (`/signal/presence/true|false`); `--own-timer` is the old
  standalone mode (do not combine). See `docs/POMODORO.md`.
- **Page switch = policy switch.** Pomodoro is OFF on the page by default: the clock does not run and `break_due` is dropped ("disabled" in the log) until
  the user switches it on (page or Keypad key). Same idea for every feature: off on the page = its triggers are off.
- **Mouse on the first `break_due`.** The plugin timer sends `break_due` once per phase, so `HapticAfterSec` for `break_due` / `break_over` is 0 (also forced in `UiSettings.Apply`,
  because an existing `settings.json` may still carry the old 5 s escalation).
- **Duck / snail / owl** are driven by `speak_up` / `pace_too_fast` / `blink_low` through the page's "screen" channel; `speak_ok`, `pace_ok`, `blink_detected` end them.
  The **Presenting** profile mutes the screen channel, so the animals do not show in that profile (harness check "presenting mutes the on-screen channel").
- **Sliders that the detectors follow** (re-read every 3 s while running, `brain/src/brain/settings.py`): Speech speed "too fast above N wpm" (-> syllables/s = wpm x 1.5 / 60;
  170 wpm = 4.25) and Speech volume "Volume floor" (page dB - 75 = dBFS, so the page default 50 = -25 dBFS, the tested threshold). Precedence: command-line flag > page > built-in default.
  `app.py --calibrate-volume` (speak normally 10 s) moves the Volume floor slider to 10 dB below your level. NOT wired (no detector yet): "too slow", target pace, measuring windows,
  "too loud", blink rate, the second-face hold time. Note: the page's default 170 wpm (4.25 syll/s) is more sensitive than the old built-in 5.0 (200 wpm); check your normal rate in the
  `rate:` log line and adjust the slider.
- **Cooldown defaults** on first page open follow the team table (pace 30 s, blink 20 min, behind 60 s, speak up 30 s) and replace the plugin's 90 s / 5 min / 20 s / 90 s.
  Blink: 20 min cooldown and no mouse buzz by default, so a demo of the blink reminder needs `--force` or a lower cooldown / the haptic channel switched on.

## Not connected yet (next phase)
1. **Devices:** `dotnet build -c Debug` in `src/` (links into Options+), drag the "Desk Coach" keys and "Coach screen" onto the Keypad, press them, test the four haptic patterns on the MX Master 4 from the page. Nothing here has run on real hardware yet.
2. **Detectors -> triggers:** `speak_up`/`speak_down` (mic), `person_behind` (second face), `volume_far` (face size), camera presence `/signal/presence/...` for Pomodoro. Only `pace_too_fast` and `blink_low` have detectors today.
3. **Screen and audio feedback:** the overlay (`/overlay`) and a sound player must read the new rule fields.
4. Team decision: blink logic (their "no blink for 8 s" vs the table's rolling 5 min rate) and pace thresholds.
