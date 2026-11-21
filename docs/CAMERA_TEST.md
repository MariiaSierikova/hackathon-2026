# Camera test: blink -> mouse buzz

Goal: prove the LogiInsight camera's own AI can drive a trigger. Each blink you make sends `test_blink`
(one soft tap) to the plugin, so the MX Master 4 taps once per blink.

State found on this Mac: camera USB ("LVNext") and its network adapter (en9) are visible; the adapter only has a
self-assigned `169.254.x.x` address, so the camera at `192.168.1.10` can't be reached yet. Wi-Fi is on a phone hotspot
(`172.20.10.x`), so there is no `192.168.1.x` clash.

## 1. Give the camera adapter a fixed address (you, ~1 min)
System Settings -> Network -> **LVNext** -> Details... -> TCP/IP -> *Configure IPv4:* **Manually**
- IP address `192.168.1.20`, Subnet mask `255.255.255.0`, Router: **leave empty** -> OK.
Check: `ping -c 2 192.168.1.10` answers.

## 2. Allow Local Network for Terminal (you)
System Settings -> Privacy & Security -> **Local Network** -> switch on the app you run commands from (Terminal / the Claude app).
Also allow **Camera** for the same app when macOS asks.

## 3. Save the camera login on this Mac (you, never in chat)
Run this in Terminal and type the user name and password from the card in your kit when prompted
(the password is not shown). It writes a private file outside the git repo:
```bash
mkdir -p ~/.config/workhealth && umask 077 && read "u?Camera user: " && read -s "p?Camera password: " && printf "LOGIWEBCAM_USER=%s\nLOGIWEBCAM_PASSWORD=%s\n" "$u" "$p" > ~/.config/workhealth/camera.env && unset u p && echo " saved"
```

## 4. Check the link
```bash
cd "/Users/imasha/Documents/Hackathon 2026/brain" && uv run python ../scripts/check_camera.py
```
Fix the first `[FAIL]` it prints (it tells you how).

## 5. Run the blink probe (90 s, then the camera AI is switched off to cool down)
```bash
cd "/Users/imasha/Documents/Hackathon 2026/brain" && uv run python src/brain/probes/blink_probe.py
```
Blink normally. Each `BLINK #n` line should come with a soft tap on the mouse.
The 5-second status lines show how many distinct AI results per second we really get (Logitech says ~27).

If the wrong camera opens (e.g. the Mac's own), add `--device 1` (try 0, 1, 2).
