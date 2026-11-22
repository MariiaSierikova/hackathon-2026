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
| Volume when far | `volume_far` (new) | same (no default feedback; the volume change is the app's job) |
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

## Overlaps with the team's Python work (read this)
- **Two Pomodoro timers.** `brain/.../pomodoro_monitor.py` (Gaze Tracker presence + focus) sends `break_due` every 2 s until you take a break. `PomodoroState` (plugin) is a separate clock with the Keypad timer and the page's settings. Run only one source of `break_due` at a time, or later let the Python monitor send only presence (`/signal/presence/true|false`) and let the plugin own the clock.
- **Page switch = policy switch.** The Pomodoro feature is off on the page by default, which sets the `break_due`/`break_over` rules to disabled. Until Pomodoro is switched on (page or key), `break_due` from `pomodoro_monitor.py` is dropped ("disabled" in the log). Same idea for every feature: off on the page = its triggers are off.
- **Pomodoro cooldown on the page defaults to 180 s** (the team's value for the repeating `break_due`), not "once per cycle", because the Python timer re-sends until the break is taken. The plugin's own timer fires once per phase either way.
- **Cooldown defaults** on first page open follow the team table (pace 30 s, blink 20 min, behind 60 s, speak up 30 s) and replace the plugin's 90 s / 5 min / 20 s / 90 s.

## Not connected yet (next phase)
1. **Devices:** `dotnet build -c Debug` in `src/` (links into Options+), drag the "Desk Coach" keys and "Coach screen" onto the Keypad, press them, test the four haptic patterns on the MX Master 4 from the page. Nothing here has run on real hardware yet.
2. **Detectors -> triggers:** `speak_up`/`speak_down` (mic), `person_behind` (second face), `volume_far` (face size), camera presence `/signal/presence/...` for Pomodoro. Only `pace_too_fast` and `blink_low` have detectors today.
3. **Screen and audio feedback:** the overlay (`/overlay`) and a sound player must read the new rule fields.
4. Team decision: blink logic (their "no blink for 8 s" vs the table's rolling 5 min rate) and pace thresholds.
