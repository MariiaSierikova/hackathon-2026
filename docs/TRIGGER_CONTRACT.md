# Trigger contract: what the camera/mic team sends, what happens next

**One sentence:** your code detects a situation (e.g. "no blink for 10 s") and sends one small JSON message to the Logi plugin
running on the same computer. The plugin decides whether and how to bother the user (mouse buzz, Keypad screen, on-screen animal).
**You never talk to the mouse, Keypad or overlay, and you don't implement cooldowns or "quiet mode".** That is the plugin's job.

```
 camera / mic code  --- JSON over HTTP, same computer --->  Logi plugin  --->  MX Master 4 buzz
 (your part)            POST http://127.0.0.1:8787/trigger   (policy)     --->  MX Keypad screen
                                                                          --->  on-screen animal
```

## 1. What a trigger looks like
```json
{ "trigger": "blink_low", "severity": 0.7 }
```
| Field | Type | Meaning |
|---|---|---|
| `trigger` | string | One of the names in section 3 (case-sensitive). Unknown name -> HTTP 400. |
| `severity` | number 0.0 - 1.0 | How clear/strong the problem is. **The plugin ignores weak ones** (Balanced level: 0.5 for pace and blink, 0.6 for eye contact and fillers). Default 1.0 if omitted. |
| `force` | `true` (optional) | Development only: skip the policy. Do not use in real detection code. |

Extra fields are ignored. Reply: `200 {"ok":true}`; `400 {"error":...}` for bad input.
The plugin must be running (Logi Options+ open, plugin loaded). Check: `GET http://127.0.0.1:8787/health` -> `{"ok":true}`.

### Send it from Python (recommended): `brain/src/brain/bridge/client.py`
```python
from brain.bridge.client import CoachClient
coach = CoachClient()                       # non-blocking, never raises, throttles repeats (2 s per trigger)

# inside your detection loop - it is safe to call every frame
if seconds_since_last_blink > 8:
    coach.trigger("blink_low", severity=min(1.0, 0.5 + (seconds_since_last_blink - 8) / 24))
if blink_just_happened:
    coach.blink_detected()                  # ends the on-screen blink reminder
coach.wpm(current_words_per_minute)         # optional, shows the number on the Keypad
```
### From anything else
```bash
curl -X POST http://127.0.0.1:8787/trigger -H "Content-Type: application/json" -d '{"trigger":"pace_too_fast","severity":0.8}'
```
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8787/trigger -ContentType "application/json" -Body '{"trigger":"pace_too_fast","severity":0.8}'
```

### The simplest way: a plain True / False
If your code only knows "problem yes/no", send exactly that. No JSON needed:
```python
import urllib.request

def pace_too_fast(flag: bool):                    # flag = True / False from your pace analysis
    urllib.request.urlopen(f"http://127.0.0.1:8787/pace/{str(flag).lower()}", timeout=1)

pace_too_fast(True)    # speech is too fast  -> snail + calm vibration (through the notification rules)
pace_too_fast(False)   # pace is fine again  -> ends the snail
```
- `GET` (or `POST`) `http://127.0.0.1:8787/pace/true` or `/pace/false` (`True`/`False`, `1`/`0` also work).
- Any alert works the same way: `/signal/<trigger>/true|false`, for example `/signal/blink_low/true`. `false` ends it
  (`pace_too_fast` -> `pace_ok`, `blink_low` -> `blink_detected`; other alerts have nothing to end).
- Optional: `?severity=0.8` (default 1.0), `?force=1` (demo: skip the notification rules).
- Python client: `coach.signal("pace_too_fast", True)`. Send it again every 1-2 s while it stays True (repeats are throttled for you).
- Answers: `{"ok":true}`, or `400 {"error":...}` for a bad value or unknown trigger.
- Note: the mouse buzz for pace waits ~5 s after the first `True` (the Keypad/animal show first). If it stays `True`, the mouse buzzes.

## 2. How often to send
- **Keep sending (about every 1-2 s) while the situation lasts, stop when it ends.** The plugin treats repeated triggers as one "episode"
  (it ends after 30 s of silence). Some alerts escalate on persistence: e.g. a pace problem shows on the Keypad first and the mouse only
  buzzes if it is still reported 5 s later.
- Do **not** add your own cooldown, "only once per minute" logic or user settings. The plugin rate-limits (cooldown, max alerts per 10 min,
  snooze, Quiet/Balanced/Attentive levels, back-off when the user dismisses alerts).
- Do send honest severities: a borderline detection should be 0.4-0.5, a clear one 0.8-1.0.

## 3. The triggers
| `trigger` | Send when | Suggested severity | What the user gets (Balanced) |
|---|---|---|---|
| `blink_low` | Blink rate dropped / no blink for a long time (normal: a blink every 3-5 s). Suggest: no blink for >= 8 s. | 0.5 at 8 s rising to 1.0 at 20 s | Pixel **eye** blinking on all 9 Keypad keys + **owl** on screen, until you send `blink_detected` |
| `blink_detected` *(control)* | **Every time a blink is seen** (debounce ~150 ms) | 1.0 (ignored) | Ends the eye/owl reminder immediately. Not an alert; skips the policy |
| `pace_too_fast` | Speaking pace above comfortable, e.g. > 170 words/min over a 20 s window | 0.5 at 170 wpm, 1.0 at 210 wpm | **Snail** crawling on the Keypad + on screen, long calm mouse vibration (after 5 s if it persists) |
| `pace_ok` *(control)* | Pace is back to normal | 1.0 (ignored) | Ends the snail early. Optional |
| `filler_burst` | Too many filler words ("um", "uh", "like"), e.g. >= 3 in 30 s | 0.6 for 3, 1.0 for 6+ | Knock on the mouse, Keypad text |
| `eye_contact_lost` | Presenter not looking at the camera/audience for > 5 s | 0.6 - 1.0 | Soft tap, Keypad text |
| `person_behind` | A second person is detected behind the user (camera AI reports only 1 face, so this needs your own face detection) | detection confidence | Red "SOMEONE BEHIND YOU" on the Keypad, double knock; allowed even while snoozed |
| `speak_up` | Speech too quiet: level of the SPEECH frames below -25 dBFS over 5 s (`pace_monitor.py`, `brain/audio/loudness.py`); keep sending every 2 s | 0.5 at -25 dBFS, 1.0 at -33 | Yellow **duck** on screen (opens its beak, "speak up!") + crisp-then-soft mouse tap (after 5 s if it persists) |
| `speak_ok` *(control)* | Voice is loud enough again (above -22 dBFS) | 1.0 (ignored) | Ends the duck early. Optional |
| `break_due` | 25 min of real work done (`pomodoro_monitor.py`, see `docs/POMODORO.md`); keep sending every 2 s until the break is taken | 0.7 | Soft triple buzz (after 5 s if it persists), generic Keypad alert |
| `lookaway_done` | After a 20-20-20 prompt the user really looked away >= 20 s | 1.0 | One soft "done" vibration |
| `speak_down` | Voice too loud | 0.5 - 1.0 | Orange "LOWER VOICE" (new; also shown as blue "SPEAK UP" / this one orange on the Keypad) |
| `volume_far` | Face box shrank / left the frame for > 2 s | 1.0 | No feedback by default; the app raises the volume (new) |
| `break_over` *(sent by the plugin's own Pomodoro timer, see `SETTINGS_UI.md`)* | A break ended | 1.0 | Red "BACK TO FOCUS" on the Keypad + haptics (new) |

Camera presence for the Pomodoro timer: `GET /signal/presence/true` (someone at the desk) or `/signal/presence/false`. Send it when it changes (and every few seconds is fine).

Not sent by you: `time_warning` ("1 minute left") is generated by the plugin's own session timer. `test_blink` is for development only.

### Optional live number
`POST /metrics` with `{"wpm": 172}` (or `coach.wpm(172)`) every 1-2 s shows words-per-minute on the Keypad's top row (green, red when too fast).

## 4. Testing without the real camera, and checking the result
```bash
python3 scripts/trigger.py --list                       # names the plugin accepts
python3 scripts/trigger.py pace_too_fast --severity 0.8 # goes through the policy, like real code
python3 scripts/trigger.py blink_low --force            # skips the policy (see it on the Keypad now)
python3 scripts/trigger.py blink_detected
```
Every decision is logged, so you can see why something did or did not show:
- macOS: `~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/WorkHealthCoach.log`
- Windows: `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\WorkHealthCoach.log`

Typical lines: `Policy: pace_too_fast dropped: severity 0.3 < 0.5`, `... dropped: cooldown (90s)`, `... -> visual only (mouse after 5s if it persists)`, `... -> haptic visual overlay`.
**If your trigger "does nothing", read this log first**: most often severity was too low or the cooldown had not passed (use `--force` to test the drawing itself).

## 5. What already sends triggers
- `brain/src/brain/monitors/blink_monitor.py`: camera -> `blink_low` / `blink_detected`.
- `brain/src/brain/monitors/pace_monitor.py`: microphone -> `pace_too_fast` / `pace_ok` (detection = `logiinsight_api/speech_flagger.py`, copied unchanged into `brain/src/brain/audio/pace.py`).
- `brain/src/brain/app.py`: runs both plus the owl/snail window in ONE command (see `docs/WINDOWS_DEMO.md`).

## 6. Questions for the team
- Who owns the 20-20-20 **20-minute timer** that starts a break? (Today there is only `lookaway_done`, the verification.) Proposal: a trigger `break_due` sent by whoever has the timer; we will add the Keypad/mouse reaction.
- Camera AI limits to design around: one face max, ~10 min continuous AI before it overheats (duty-cycle it), mesh missing in some frames. See `docs/RESOURCES.md`.
