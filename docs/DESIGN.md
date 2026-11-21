# Design decisions (updated 2026-11-21)

## 1. Our scope = react to triggers
The camera/mic analysis is someone else's input. We define a **trigger contract** and build every output against it.
Anyone (real camera code, a test tool, a teammate) sends this to `POST http://localhost:8787/trigger`:

```json
{ "trigger": "pace_too_fast", "severity": 0.0-1.0, "data": { "wpm": 190 } }
```

| trigger | meaning | default outputs |
|---|---|---|
| `pace_too_fast` | speaking too fast | long calm haptic, snail overlay, keypad pace meter red |
| `time_warning` | presentation time nearly up (user sets minutes) | quick double haptic, keypad timer flashes |
| `filler_burst` | too many filler words | keypad counter, overlay parrot (optional) |
| `eye_contact_lost` | not facing audience/camera | overlay owl, keypad icon |
| `blink_low` | blink rate dropped | double-tap haptic, keypad blinking eye |
| `lookaway_done` | 20 s break completed | single soft haptic, keypad check |
| `person_behind` | someone behind you | haptic knock, keypad alert, overlay meerkat |

A tiny test tool (`scripts/trigger`) sends any of these, so the whole feedback side is built and demoed without a camera.

## 2. Never annoying: user-controlled notification policy
Judges want a product people keep using. Rule: **notify only when it matters, and let each person tune it.**
- **Presets:** Quiet (extreme cases only), Balanced (default), Attentive.
- **Per trigger:** on/off, channels (keypad / haptic / overlay), minimum severity, cooldown, max alerts per 10 min.
- **Escalation ladder:** keypad/overlay first; haptic only if the problem persists N seconds.
- **Haptic strength:** Light / Normal / Strong = different waveforms + pulse count (and Options+ mouse haptic level).
- **Snooze:** one Keypad key = snooze 15 min. **Adaptive back-off:** dismissed twice -> stay quiet longer.
- **No praise/positive buzzes.**
- Settings live in a human-readable file and a small local settings page; the Keypad Mode key cycles presets.

## 3. Presenter animal overlay: must-have, share-safe
- Transparent always-on-top window on the presenter's screen only.
- Risk: macOS 15 may ignore the "exclude from capture" window flag for new capture APIs. **Test early on this Mac** (record the screen / Zoom share).
- Reliable fallback: presenter shows the animal on their own screen, shares one window or a second display. App shows a "share-safe" check and tips.

## 4. Session report (optional demo closer)
One summary screen after a session: pace over time, filler count, eye-contact %, time per slide, number of alerts. Data = a log of triggers + metrics.
