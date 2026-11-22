# Demo for the Logitech team (10 minutes)

**One sentence:** Desk Coach is a quiet work-health coach. The camera and microphone notice what you cannot (you stopped blinking, you skipped a break, someone is standing behind your headphones),
and one Logi Options+ plugin decides whether it is worth bothering you, then tells you through the devices you already touch: MX Master 4 buzz, MX Keypad, Actions Ring, screen. Everything runs locally.

**Only one minute?** `docs/PITCH_1MIN.md` (one live moment, the rest in a sentence). The Demo page has a **1 min jury** button for it.

**Short version to read aloud:** `docs/DEMO_SCRIPT.md` (one page). The rest of this file is the detail behind it.

**Presenter view:** open `http://127.0.0.1:8787/#/demo` (works offline, served by the plugin). It has the 10-minute clock, the current part highlighted, the lines to say, and a one-click fallback for every scene.
Put it on a second screen. The text below is the same content.

## Running order (10:00)

| Time | Part | You do | The audience sees | If it fails |
|---|---|---|---|---|
| 0:00 | **The problem** (45 s) | Headphones on, music playing | - | - |
| 0:45 | **How it fits** (45 s) | Show the architecture slide or the dashboard | camera/mic -> detectors -> C# plugin in Options+ (the rulebook) -> mouse, keypad, ring, screen | - |
| 1:30 | **You stay in control** (60 s) | Press a Desk Coach key: that widget goes grey and the page switch turns off. Flip a switch on the page: the key changes colour. Open Customize once. Press Presenting | One key greys, not all; four feedback types; cooldown; silent profile | Do it from the page only |
| 2:30 | **Speech: pace and volume** (60 s) | Say a sentence slowly, then read fast for 10 s. Then speak very quietly for 5 s | Snail on screen and a calm buzz; yellow duck "speak up" | Buttons "Fallback: too fast" / "too quiet" |
| 3:30 | **Blinking** (45 s) | Stare without blinking for about 6 s, then blink | Owl on screen, one big eye across the keypad, then gone | Button "Fallback: owl now" |
| 4:15 | **Pomodoro that knows you left** (75 s) | "Use 1-minute demo timer", press the Pomodoro key, walk away, come back | Timer on the key and the keypad, pauses when you leave, break reminder + buzz | "Fallback: break reminder now". Afterwards press "Back to 25 / 5 / 15" |
| 5:30 | **Volume when far** (60 s) | Music on speakers; step back; leave the frame; return | Volume rises gradually, mutes after you left, comes back and restores | "Fallback: step back" / "leave the frame" (changes the real volume) |
| 6:30 | **Person behind: the headline** (120 s) | Headphones on. A colleague walks past, then walks up to you. Open the Actions Ring | Walking past: nothing. Coming to you: mouse buzz, all nine keypad widgets show one red picture, notification. Ring: Talk mode, Hide my screen, It is fine | "Fallback: someone walks past / comes to my desk", ring buttons |
| 8:30 | **Brightness: needs the hardware** (45 s) | Open the locked "Screen brightness" card | A concept card with the planned behaviour | - |
| 9:15 | **What we would love from Logitech** (45 s) | Leave the page up | - | - |

Questions after 10:00. If you run long, drop part 5 (Volume when far) first, then part 4 (Blinking).

### Say about brightness (do not skip)
"Brightness is the one feature we show as a concept. The LogiInsight camera does not report brightness or ambient light; Logitech confirmed that. So it has to be enabled in the camera hardware.
With a camera that exposes it, the same engine adjusts the monitor, the keyboard backlight and the room lights. Today we can only show it with a laptop camera that has brightness control."

### What we would love from Logitech (closing)
1. Brightness / ambient light from the camera (a hardware or firmware switch).
2. A second person detected on the camera itself (the LogiInsight AI reports one face).
3. A settings panel inside Options+ for plugins (today: a local page).
4. Haptic duration and strength (today: fixed waveforms; long = several pulses).
5. A plugin that can open the Actions Ring or a Keypad folder (today: the user opens it).

## Before you go on stage (15 minutes)
Use the machine that has the camera and the devices (the face AI needs **Windows**; on a Mac only the infrared stream is visible).
1. `git pull`, `dotnet build -c Debug` in `plugin/WorkHealthCoachPlugin/src`, restart the Logi Plugin Service, and check `Invoke-RestMethod http://127.0.0.1:8787/health` shows a build time from minutes ago.
2. Keypad: the nine Desk Coach keys in this order, or the alert picture is scrambled:

   | | column 1 | column 2 | column 3 |
   |---|---|---|---|
   | row 1 | Speech speed | Speech volume | Eye blinking |
   | row 2 | Pomodoro | Volume when far | Person behind |
   | row 3 | Master switch | Presenting mode | Alert light |
3. Actions Ring (MX Master 4): the six actions under "Desk Coach: person behind". Check "Hide my screen" (your shortcut) and "Pause / resume music" once; the music key is the least certain on a Mac.
4. Camera side: Gaze Tracker running and calibrated from your seat; camera login in the environment variables (never paste it anywhere); then
   `cd brain` and `uv run python src/brain/app.py --auto --mic M --ir-device N` (`--list` shows the numbers; see `docs/AUTO.md`). Blink is started separately (`docs/WINDOWS_DEMO.md`) and the camera AI must not run more than about 10 minutes: start it just before the blink part.
5. Page: open `http://127.0.0.1:8787/#/demo` and press **Check everything**: the plugin answers, master and alert light are on, volume is readable, and every feature you plan to show is switched on (Pomodoro is off by default: switch it on or let the demo button do it).
6. Sound: music playing at a volume you can hear, speakers (not headphones) for "Volume when far"; headphones on for "Person behind". Close chat and notification apps. Screen sharing off for the overlay unless you want it seen.
7. Rehearse the walk-past and walk-up with your colleague once. The infrared second-face detector sees frontal faces about 1 to 1.5 m from the camera.

## What is live and what is simulated
Every camera or microphone scene has a **Fallback** button on the Demo page that sends the same event the detector would send, so the plugin, mouse, keypad and screen react exactly the same.
Say which one you use ("here I trigger it by hand"). Nothing is hidden: the settings, cooldowns, keys, ring actions and the Pomodoro/volume logic are the real ones either way.

| Part | Live path | Needs |
|---|---|---|
| Control (keys <-> page) | real | Options+ and the Keypad |
| Speech pace / volume | microphone | `--mic M` (works on Mac and Windows) |
| Blinking | LogiInsight face mesh | Windows, camera login |
| Pomodoro, Volume when far | Gaze Tracker (presence, face size) | Windows, calibrated |
| Person behind | infrared second-face detector | `--ir-device N`, frontal faces, 1 to 1.5 m |
| Brightness | not built (concept) | camera that reports brightness |

## Known limits to know before someone asks
- Not run by us on a real camera end to end: the infrared picture, second-face detection on real faces and the Gaze Tracker face size as a distance (see `docs/CAMERA_FEEDS.md`).
- The thresholds (walk past versus coming, the distance change) are reasoned defaults. The pace limit is the team's value (about 5 syllables a second).
- The Windows volume code was written from the documentation and has never run on Windows. On a Mac it is tested.
- "Hide my screen" presses a shortcut you recorded; on a Mac the default only hides the other apps.
- "Show my screen again" does nothing on a Mac until a bring-back shortcut is set.
- The camera AI overheats after about 10 minutes of continuous use; the blink monitor stops after 8.
- No video leaves the computer; there is no cloud part.

## Questions they may ask
- **Is it annoying?** Every alert goes through one policy: minimum severity, cooldown, a budget per 10 minutes, snooze, back-off when you dismiss alerts, a silent Presenting profile and one Alert light key to switch everything off.
- **Privacy?** All processing is local (localhost only). No recording, no upload.
- **Why a plugin and a local page?** The plugin API has no settings panel; the page is served by the plugin, so there is nothing else to install.
- **Why not on the camera itself?** The camera AI gives one face and a face mesh; our other detectors run on the PC. That is part of the wishlist.

## Slides (3, optional)
1. **Problem and promise:** headphones, screens, skipped breaks; "a coach that only speaks when it matters, on devices you already use".
2. **Architecture:** camera / microphone -> detectors -> plugin (policy) -> MX Master 4, MX Keypad, Actions Ring, screen; "local only".
3. **Wishlist for Logitech** (the five points above, brightness first).
