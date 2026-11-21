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


---
# Result of the first test on the Mac (2026-11-21)
- Link works: network adapter, SSH login, firmware check all `PASS` (`scripts/check_camera.py`).
- **On macOS the camera shows only its infrared stream ("LVNext CPD Camera")**; the colour "LVNext Camera" is not offered by macOS
  (Logitech: colour does not work on Macs). The camera's built-in face AI runs only on the colour stream, so **no AI results on a Mac**
  and `blink_probe.py` saw 0 results. Mic ("LVNext Microphone") works on the Mac.
- **Next: run the same probe on the teammate's Windows laptop** (colour camera + AI available there). Nothing in the plugin depends on the camera.

## Windows steps (teammate laptop)
1. Install: Logi Options+, .NET 10 SDK, `uv` (https://docs.astral.sh/uv/), Git. `git clone https://github.com/MariiaSierikova/hackathon-2026`.
2. Build the plugin: `cd plugin\WorkHealthCoachPlugin\src` then `dotnet build -c Debug` (creates the plugin link; restart Logi Plugin Service in Options+ if needed).
3. Camera network: `Win+R` -> `ncpa.cpl` -> the new LVNext/NCM adapter -> IPv4 properties -> IP `192.168.1.20`, mask `255.255.255.0`, gateway/DNS empty. Turn Wi-Fi off if it uses 192.168.1.x.
4. Credentials (cmd, this window only; never commit): `set LOGIWEBCAM_USER=...` and `set LOGIWEBCAM_PASSWORD=...` (values on the kit card).
5. `cd brain` then `uv run python ../scripts/check_camera.py`, then `uv run python src/brain/probes/blink_probe.py --seconds 60`
   (add `--device 0/1/2` if the wrong camera opens; use `--format NV12` only if you also need IR).
