# One-minute pitch for the jury

One minute cannot show eight features. So: **one live moment, everything else in one sentence, one ask.**
The live moment is **Person behind** (headphones on, a colleague walks up): it is easy to understand without explanation and uses the most Logitech parts at once
(MX Master 4 buzz, MX Keypad, mouse ring, camera).

**Presenter view:** `http://127.0.0.1:8787/#/demo`, press **1 min jury**. It shows a 60-second clock, the five beats below, and a Fallback button for the live moment.
The 10-minute version is the **10 min** button on the same page (`docs/DEMO_SCRIPT.md`, `docs/DEMO.md`).

## The five beats (60 s)

| Time | Beat | What you do |
|---|---|---|
| 0:00 | **Hook** | Headphones on, music playing |
| 0:15 | **The live moment** | A colleague walks past behind you (nothing happens), then walks up to your chair (the mouse buzzes, the keypad flashes red) |
| 0:33 | **The mouse ring** | Open the ring, press Talk mode: the music goes down |
| 0:40 | **Everything else** | Point at the keypad while you say the sentence |
| 0:52 | **Brightness and thank you** | The ask |

## The speech (about 120 words)

**[Headphones on, music playing]**
> "We all work with headphones on. We stop blinking, skip breaks, and cannot hear someone walking up behind us.
> Desk Coach is a quiet coach that runs on your own computer and talks to you through the Logitech devices you already use."

**[A colleague walks past: nothing. Walks up: mouse buzzes, keypad flashes.]**
> "Walking past: nothing. Coming to me: my MX Master 4 buzzes and my keypad flashes."

**[Open the mouse ring, press Talk mode.]**
> "And from the mouse ring I lower the music."

**[Point at the keypad.]**
> "The same engine watches my pace, my voice, my blinking, my breaks and my distance from the screen.
> Every feature has a switch on the keypad, and nothing leaves this laptop."

> "One feature needs Logitech: screen brightness. The camera must report ambient light, a hardware switch we would love. Thank you."

## Rehearse these three things
1. **The walk-up**, with your colleague, at least twice. It is the one thing that can fail on stage.
2. **Talk mode** once (press it again afterwards to restore the volume). Say only what you have rehearsed: if "Hide my screen" has no checked shortcut, leave it out.
3. **The timing**, aloud, with the clock. The words fill about 45 seconds and the walk-up fills the rest. Over time: cut the "Everything else" sentence down to "pace, voice, blinking, breaks, distance, all on the keypad".

## If the camera fails
Press **Fallback: someone comes to my desk** on the page. It sends the same event as the camera, so the buzz and the keypad flash are identical. Say "here I trigger it by hand".

## Keep one line ready for each feature (for questions)
| Feature | One line |
|---|---|
| Speech pace / volume | "Local microphone analysis: a snail asks me to slow down, a duck asks me to speak up." |
| Blinking | "The camera's face mesh sees I stopped blinking; an owl reminds me." |
| Pomodoro | "A focus timer that only counts the time I am really at the desk, and resets after five minutes away." |
| Volume when far | "The sound gets louder as I move away and mutes when I leave, then my volume comes back." |
| Brightness | "A concept: it needs the camera to report brightness or ambient light, which the LogiInsight camera does not do (Logitech confirmed it). With a camera that does, the same engine adjusts the monitor, keyboard backlight and room lights." |
| Quiet by design | "Every alert goes through one policy: cooldowns, a budget, snooze, a silent Presenting mode, and an Alert light key that switches everything off." |

## Honest answer if asked "is it real?"
The settings, keys, ring actions, rules and the person-behind decision are the real code. The camera and microphone detectors are real too, but the infrared second-face detector and the
Gaze Tracker distance have not been verified on a real camera end to end (`docs/CAMERA_FEEDS.md`). If you used a Fallback button, say so.
