# Windows demo: owl when you don't blink, snail when you talk too fast (ONE window)

Everything runs from **one PowerShell window** with **one command**. The Logi plugin runs in the background inside Logi Options+
(nothing to start). Project folder below: `C:\LauzHack\hackathon-2026` (change it if yours is elsewhere).
You already did the one-time setup in `docs/CAMERA_TEST.md` (parts A and B). This page only adds the new steps.

## 1. Update everything (2-3 minutes)
```powershell
cd C:\LauzHack\hackathon-2026
git pull
cd plugin\WorkHealthCoachPlugin\src
dotnet build -c Debug
```
The last lines must say `Build succeeded.` Then in **Logi Options+ -> Settings -> Restart Logi Plugin Service** (wait 15 s).
If `dotnet build` says a file is in use, close Logi Options+ completely (also in the system tray), build again, start Options+ again.

```powershell
cd ..\..\..\brain
uv sync
Invoke-RestMethod http://127.0.0.1:8787/health
```
`uv sync` installs the new libraries. The last command must print `ok : True` and a `build` time that is **a few minutes old at most**
(= your build just now). If the time is old, Windows is still running the old plugin: repeat the restart.

## 2. Find your camera and microphone numbers
```powershell
uv run python src\brain\app.py --list
```
- **Microphones:** note the number of `LVNext Microphone`. If it appears several times, take the one with **2 ch** (your teammate used `11`).
- **Cameras:** note the number of the **colour** camera (the one that is not grey/tall). Call these `N` (camera) and `M` (microphone) below.

## 3. Test 1 - everything without any hardware (50 seconds)
```powershell
uv run python src\brain\app.py --simulate --no-blink-after 5
```
You should see, in this one window and on the screen:
| Time | What happens |
|---|---|
| 0 s | `[app] Overlay window ready`, `[blink] Plugin answers (build ...)`, simulated talker at a normal pace |
| 3 s, 6 s | `[blink] blink #1`, `blink #2` |
| ~11 s | `[blink] >> no blink for 5s ... blink_low sent` -> the **owl** appears bottom-right with "blink!" |
| 20 s | `[blink] blink #3` -> the owl **disappears** |
| 26 s | `[pace] >> too fast: 6.2 syllables/s ...` -> the **snail** crawls along the bottom with "slow down!" (and the Keypad snail, if you have the Keypad) |
| ~31 s | the mouse gives a calm long vibration (the plugin waits 5 s of continued fast speech before buzzing) |
| 42 s | `[pace] pace is fine again` -> the snail **disappears** |
| 50 s | the window closes itself |

If this works, the plugin, the overlay and the rules are fine on this laptop.

## 4. Test 2 - your voice only (no camera needed)
Say a sentence slowly, then read a text **very fast** for ~10 seconds:
```powershell
uv run python src\brain\app.py --no-blink --mic M --force
```
(`--force` = demo mode, ignores the "don't nag more than every 90 s" rule.)
- It prints every 3 s: `rate: 4.1 syllables/s | speaking: yes | ok`. Normal speech is about 3-4.5, too fast is **5 or more** (your teammate's threshold).
- When you talk too fast: `>> too fast ... pace_too_fast sent` -> snail on screen, then the mouse buzzes.
- When you slow down (below 4.3) or stop talking: `pace is fine again` -> the snail leaves.
- If it never says "too fast" even when you gabble, or says it all the time: tune the limit, e.g. `--fast-rate 4.5 --ok-rate 3.8`.
Stop with `Ctrl+C`.

## 5. Test 3 - the real thing: camera + microphone together
First type the camera login in this window (see `CAMERA_TEST.md`, step A7):
```powershell
$env:LOGIWEBCAM_USER = "user-from-the-card"
$env:LOGIWEBCAM_PASSWORD = "password-from-the-card"
```
Then:
```powershell
uv run python src\brain\app.py --device N --mic M --no-blink-after 6 --force --log ear_log.csv
```
- Blink normally: `[blink] blink #...` lines.
- **Stare without blinking for ~6 s** -> owl; **blink** -> owl gone.
- **Talk very fast** -> snail; slow down -> snail gone.
- It runs 8 minutes for the camera (so the camera does not overheat), then the camera part stops; `Ctrl+C` stops everything earlier.
- Only one animal shows at a time (the latest one wins).

## 6. Send back
- The text of the window (select all, copy) from Test 1 and Test 3, and the file `ear_log.csv` (in the `brain` folder).
- Did the owl and the snail appear and disappear when expected? Did the mouse buzz?
- How many blinks you counted yourself vs. the `blink #` lines.

## If something goes wrong
| Problem | What to do |
|---|---|
| `[blink] WARNING: the Logi plugin does NOT answer` | Restart the Logi Plugin Service (step 1); check `Invoke-RestMethod http://127.0.0.1:8787/health` |
| `Which camera? Run with --list` | Add `--device N` (step 2), or `--no-blink` to skip the camera |
| `[blink] ERROR: camera credentials not found` | Type the two `$env:` lines in THIS window (step 5) |
| `[pace] ERROR: cannot open the microphone` | Use the 2-channel `LVNext Microphone` number: `--mic M`; allow microphone access: Settings -> Privacy & security -> Microphone -> desktop apps |
| Owl/snail never appears but the log says the trigger was sent | The plugin's rules dropped it: use `--force`, and check `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\WorkHealthCoach.log` |
| The animal stays after you blinked / slowed down | Send the window text; the overlay prints `plugin says animal: owl -> None` when it hides |
| `No module named ...` | `uv sync` in the `brain` folder |
