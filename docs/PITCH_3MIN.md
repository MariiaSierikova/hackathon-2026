# 3-minute demo + 1 minute of questions (two versions)

Same product, same live moment, two audiences:
- **Jury version:** plain words, the story and the experience.
- **Logitech version:** the same demo, but more technical: the stack, the Logitech APIs we used, and what we need from Logitech.

Both say two things clearly: **everything runs locally**, and **the backend is built in C#** (a plugin for Logi Options+).
Be exact about the C# part: the *backend* (rules, settings, local server, haptics, keypad, ring) is C#. The camera and microphone *detectors* are small Python programs
(the LogiInsight SDK and the Gaze Tracker are Python / WebSocket) that only send events to the C# backend. Say that yourself before someone asks.

**Presenter view:** `http://127.0.0.1:8787/#/demo` has three buttons: **3 min jury**, **3 min Logitech**, **10 min**. Each shows a 4-minute clock (3 minutes of demo + 1 minute of questions),
the current part, the lines, and a Fallback button for every scene that depends on a camera. During the questions, the page lists the short answers.

---

## Version 1: for the jury (3:00 + 1:00 questions)

| Time | Part | You do |
|---|---|---|
| 0:00 | Hook | Headphones on, music playing |
| 0:25 | Local, built in C# | - |
| 0:40 | **The live moment: person behind** | A colleague walks past (nothing), then walks up (mouse buzzes, keypad flashes) |
| 1:40 | The mouse ring | Open the ring, press Talk mode |
| 2:00 | You stay in control | Press a keypad key, flip the same switch on the page; mention silent Presenting mode |
| 2:30 | The rest in one sentence | Point at the keypad |
| 2:48 | Brightness and thank you | - |
| 3:00 | Questions (1 min) | - |

**Say** (about 205 words, the rest of the time is the live moment):

> **0:00** "We all work with headphones on. We stop blinking, skip breaks, and cannot hear someone walking up behind us.
> Desk Coach is a quiet coach that looks after that, through the Logitech devices you already use."
>
> **0:25** "Everything runs on this laptop. No video and no audio leave it. The brain of the app is a C# plugin inside Logi Options+."
>
> **0:40** "Watch behind me. Someone walks past: nothing, we ignore that. Now someone walks up to my desk: my mouse buzzes and my keypad flashes. I would never have heard them."
>
> **1:40** "From the mouse ring I choose what to do: lower the music so I can talk, hide my screen, or say it is fine."
>
> **2:00** "It never nags. Every feature has a switch, on the keypad and on this page. I press a key and it turns off here too. When I present, one switch makes it silent for the audience."
>
> **2:30** "The same engine also watches my speaking pace and volume, my blinking, my breaks, and my distance from the screen."
>
> **2:48** "One feature needs Logitech: screen brightness. The camera has to report ambient light. That is a hardware switch we would love. Thank you."

**Questions the jury may ask** (one-line answers, also shown on the Demo page):
- *Is it annoying?* "One policy for everything: cooldowns, a budget, snooze, a silent Presenting mode and one key that switches all alerts off."
- *Privacy?* "Everything runs on this computer. Nothing is recorded or uploaded."
- *How accurate is it?* "It is a prototype with reasoned defaults, and the settings are adjustable."
- *What next?* "Brightness from the camera, and a second person detected on the camera itself."

---

## Version 2: for the Logitech team (3:00 + 1:00 questions, technical)

| Time | Part | You do |
|---|---|---|
| 0:00 | Hook | Headphones on |
| 0:15 | The architecture | Show the dashboard or one slide |
| 0:50 | **The live moment: person behind** | Walk past, then walk up |
| 1:45 | The devices | Mouse buzz, keypad picture, open the ring |
| 2:15 | Settings and control | Press a key and flip the page switch |
| 2:35 | Brightness and our wishlist | - |
| 3:00 | Questions (1 min) | - |

**Say** (about 245 words):

> **0:00** "People wear headphones, stare at screens and skip breaks. We built a coach on your own stack: the LogiInsight camera, MX Master 4, MX Keypad and the Actions Ring."
>
> **0:15** "Everything runs locally. The backend is a C# plugin on the Logi Actions SDK, .NET 10, running inside Logi Plugin Service. It listens on localhost, port 8787.
> Small Python detectors feed it events: the LogiInsight face mesh, the Gaze Tracker, an infrared second-face detector and the microphone. The plugin owns every rule and every output."
>
> **0:50** "Person behind. The infrared stream gives us faces with a position and a size, and the plugin follows each one. Sideways speed, measured in face widths per second, separates walking past from coming to me.
> Walk past: ignored. Walk up: alert."
>
> **1:45** "The alert goes through one policy: severity, cooldown, a budget, escalation. Then three outputs: haptic events mapped to MX Master 4 waveforms,
> one picture across all nine keypad keys as widgets, and Actions Ring commands I can press without the keyboard."
>
> **2:15** "The settings are a page the plugin serves from its own DLL. Keys and page stay in sync in both directions."
>
> **2:35** "Brightness stays a concept: the camera does not report ambient light, so it has to be enabled in the hardware.
> Our wishlist: ambient light and a second person from the camera, haptic duration and strength, a settings panel for plugins in Options+, and opening the Ring or a folder from a plugin."

**Technical facts to have ready** (true as built, checked against the code and docs):
- C# plugin on .NET 10 using `LogiPluginApi`; inside it a plain loopback `TcpListener` server (`HttpListener` needs admin rights on Windows); policy, settings, Pomodoro, volume and person-behind decisions are C#.
- Detectors send `POST /trigger`, `/behind`, `/distance` and `/signal/presence/...`; contract in `docs/TRIGGER_CONTRACT.md`.
- Haptics: four user-selectable patterns mapped to `sharp_state_change`, `damp_collision`, `angry_alert`, `jingle` in `eventMapping.yaml`.
- Keypad: widget commands with `IsWidget`, one 3x3 picture, each key redrawn by name with `ActionImageChanged(key)`.
- Ring: six `PluginDynamicCommand` parameters (Hide my screen, Show my screen again, Pause / resume music, Talk mode, It is fine, Snooze).
- Settings page: embedded resources, served by the plugin; state in `ui-state.json`; no panel API for plugins in Options+.

**Questions Logitech may ask** (short answers, also shown on the Demo page):
- *Why Python for the detectors?* "The LogiInsight SDK and the Gaze Tracker are Python and WebSocket. Only events cross to the C# backend, so detection and rules stay separate."
- *Latency?* "Not measured. Rules run per event, and the Keypad redraw is limited to about ten per second."
- *Platforms?* "The plugin runs on both. The face AI needs Windows (macOS shows only the infrared stream). The Windows volume code is untested."
- *Limits?* "One face from the camera AI, the infrared second-face detector works at about 1 to 1.5 m, the camera AI overheats after about 10 minutes, haptics have no duration."

---

## Before you go on (both versions)
1. Same checklist as `docs/DEMO.md` ("Before you go on stage"): build and restart the plugin, keypad keys in the right order, the six ring actions, camera side running, then **Check everything** on the Demo page.
2. Rehearse the walk-past and walk-up with your colleague at least twice, and say the C# and local sentences aloud once.
3. Rehearse Talk mode once (press it again afterwards to restore the volume). Say only what you have rehearsed.
4. Time it with the Demo page clock: 3:00 of demo, then the questions.

## If the camera fails
Press **Fallback: someone comes to my desk** on the page. It sends the same event as the camera. Say "here I trigger it by hand".

## What is not verified
The infrared second-face detector and the Gaze Tracker distance on a real camera end to end (`docs/CAMERA_FEEDS.md`), the Windows volume code, and the Actions Ring "Hide my screen" shortcut on your machine.
