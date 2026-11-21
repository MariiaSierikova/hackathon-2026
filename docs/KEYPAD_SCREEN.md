# Coach screen on the MX Keypad (replaces the small tile widgets)

One Keypad key ("Coach screen", group **Coach**) opens a folder that uses all 9 keys as ONE big picture, drawn in chunky pixel art.

## Normal (idle) view - big and readable
- Top row: `SLIDE n` + time on the slide; words-per-minute (green, red when too fast) if the pace is known
- Middle row: **huge session timer** MM:SS (grey = not started, green, amber, red in the last minutes)
- Bottom row: **Prev slide**, **Play/Pause**, **Next slide** (sends Left/Right arrow keys to your presentation app)
- Press any timer key = start/pause

## Reminders (only when needed; pass the notification policy first)
| Trigger | Takes over all 9 keys with | Ends |
|---|---|---|
| `blink_low` | cute pixel **eye** that keeps blinking, "BLINK!" | when the camera sends `blink_detected` (you blinked), or any key press, or 90 s |
| `pace_too_fast` | cute pixel **snail** crawling across, "SLOW DOWN" | after 15 s, `pace_ok`, or any key press |
| `time_warning` | flashing red "TIME xx:xx LEFT" | 10 s |
| `person_behind` | flashing red "SOMEONE BEHIND YOU" | 12 s |
Any key press while a reminder shows dismisses it (counts toward the coach getting quieter).

Camera side contract (new): send `blink_detected` every time a blink is seen; the eye reminder stops by itself.
Testing: run `python3 scripts/trigger.py blink_low --force` ALONE, look at the Keypad, then `python3 scripts/trigger.py blink_detected`. (`--force` skips cooldown/budget/snooze; without it the policy may drop repeats.)

## Art
`scripts/gen_sprites.py` draws eye / snail / owl sprites -> `assets/sprites.json` (used by the on-screen overlay) and
`plugin/.../Ui/SpriteData.g.cs` (used by the Keypad). Preview sheets: `assets/sprites_preview_light.png` / `_dark.png`.
Run: `uv run --project brain --with pillow python scripts/gen_sprites.py`.

## Try it
Options+ -> MX Keypad -> All Actions -> "Coach screen" -> drag onto a key, press it to open the folder. Then:
```bash
python3 scripts/trigger.py --wpm 172
python3 scripts/trigger.py blink_low --force   # eye blinks on all 9 keys (run alone)
python3 scripts/trigger.py blink_detected # ...and stops
python3 scripts/trigger.py pace_too_fast  # snail crawls (the mouse buzzes after 5 s if repeated: escalation)
```
Tip: the Balanced level allows blink reminders every 5 minutes; switch to Attentive while testing:
`curl -X POST 127.0.0.1:8787/preset -d '{"name":"attentive"}'`
