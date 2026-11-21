# On-screen animals (presenter only)

`overlay/overlay.py` draws cute pixel animals on your screen, **click-through** and always on top:
- **Snail** crawls from the bottom-left to the bottom-right (about 14 s) with "slow down!" when you speak too fast
- **Owl** sits in the bottom-right corner, bobbing and blinking, with "blink!" until you blink

It follows the Logi plugin (`GET /overlay`), so it respects the notification policy (quiet level = fewer animals).
Sprites come from `assets/sprites.json` (made by `scripts/gen_sprites.py`).

## Run
```bash
uv run --project brain python overlay/overlay.py              # follows the plugin
uv run --project brain python overlay/overlay.py --demo owl   # no plugin needed (also: --demo snail)
```
Stop it with Ctrl+C in that terminal (or `pkill -f overlay.py`).

## Hidden from screen sharing: how and how sure we are
- **macOS:** `NSWindowSharingNone` + status window level (also over full-screen apps).
  macOS 15 may ignore this for newer capture apps (ScreenCaptureKit), so it has to be **tested per app**.
- **Windows 10 2004+/11:** `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` - reliable for Zoom/Teams/Meet/OBS window and screen capture.
- Always-safe fallback: share one window (e.g. the slides) or the second display, never the display with the animals.

### Capture test (do this before relying on it)
1. Run the overlay, then show the owl for 90 s: `python3 scripts/trigger.py blink_low --force` (stop it with `blink_detected`).
2. Take a screenshot (Cmd+Shift+3 / Cmd+Shift+5) - is the owl in the picture?
3. Record the screen in QuickTime (File -> New Screen Recording) - owl in the video?
4. Share your screen in Zoom / Meet / Teams to a second device - owl visible there?
Write down which ones hide it; we add a warning in the app for the ones that don't.
