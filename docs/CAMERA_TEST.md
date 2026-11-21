# Camera test on a clean Windows laptop

**Goal:** prove that the LogiInsight camera's own AI can tell when you blink, and that each blink can drive our feedback
(a tap on the MX Master 4).

**Why Windows:** on a Mac the camera only offers its infrared picture, and the face AI needs the colour picture
(Logitech: colour does not work on Macs). On Windows both work.

**Time:** about 20-30 minutes the first time (most of it is downloads). Words in `code style` are things you type or click.

## What you need
- The LogiInsight camera + its USB-C cable, plugged into a **USB 3** port (the blue or "SS" one is best), and the **card from the kit**
  with the user name and password.
- Windows 10 or 11 with internet, and Logi Options+ already installed.
- **Part B only:** an MX Master 4 mouse connected to Options+ (the Keypad is optional). If you don't have the mouse, do Part A only.

## Part A - the camera detects your blinks (no Logitech devices needed)

### A1. Open PowerShell
Press the Windows key, type `PowerShell`, press Enter. Use this one window for everything below.
(If a step says "close and reopen PowerShell", do exactly that.)

### A2. Install the three tools
Copy each line, paste it, press Enter. Say yes to any agreement question.
```powershell
winget install --id Git.Git -e --accept-package-agreements --accept-source-agreements
winget install --id astral-sh.uv -e --accept-package-agreements --accept-source-agreements
winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
```
- **Git** downloads our project. **uv** installs the right Python (3.12) and our libraries by itself. **.NET 10** is only needed for Part B but takes a minute, so install it now.
- Now **close PowerShell and open a new one** (so the new tools are found), then check:
```powershell
git --version
uv --version
dotnet --version
```
Each should print a version (`dotnet` should start with `10.`). If `winget` is not found, install "App Installer" from the Microsoft Store, or download the tools from
https://git-scm.com , https://docs.astral.sh/uv/getting-started/installation/ and https://dotnet.microsoft.com/download/dotnet/10.0 .

### A3. Get the project
```powershell
cd $HOME\Documents
git clone https://github.com/MariiaSierikova/hackathon-2026.git
cd hackathon-2026\brain
```
(No Git? On the GitHub page of the project click **Code -> Download ZIP**, unzip it, and open PowerShell in the `brain` folder.)

### A4. Install our Python libraries (one time, a few minutes)
```powershell
uv sync
```
It downloads Python 3.12 and the libraries. Wait until it finishes without a red error.

### A5. Plug in the camera and wait one minute
Connect the camera with the USB 3 cable and wait about 60 seconds (it boots).
Check: press `Win + R`, type `devmgmt.msc`, Enter. Under **Cameras** you should see `LVNext Camera` (colour) and `LVNext CPD Camera` (infrared).
Allow apps to use the camera: **Settings -> Privacy & security -> Camera -> Let desktop apps access your camera: On**.

### A6. Give the camera's network adapter a fixed address
The camera sends its AI results over a small virtual network cable inside the USB cable. Windows needs to know your address on it.
1. Press `Win + R`, type `ncpa.cpl`, Enter. A **new Ethernet adapter** appeared when you plugged in the camera (its name may contain *LVNext*, *USB*, *NCM*, or it just says *Unidentified network*).
   - **No new adapter?** Open `devmgmt.msc`. If you see `CDC NCM` under *Other devices*: right-click -> *Update driver* -> *Browse my computer* -> *Let me pick from a list* -> *Network adapters* -> *Microsoft* -> **UsbNcm Host Device**. Also try another USB 3 port or cable.
2. Right-click that adapter -> **Properties** -> double-click **Internet Protocol Version 4 (TCP/IPv4)** -> **Use the following IP address**:
   - IP address: `192.168.1.20`
   - Subnet mask: `255.255.255.0`
   - Default gateway and DNS: **leave empty** -> OK -> OK
3. **Turn Wi-Fi off if it also uses `192.168.1.x`.** Check with:
```powershell
ipconfig | findstr "192.168.1"
```
   You should see only the camera adapter (`192.168.1.20`). If your Wi-Fi shows `192.168.1.something` too, switch Wi-Fi off during the test (or use a phone hotspot).
4. Test the link (the first ping may time out, the others must answer):
```powershell
ping 192.168.1.10
```
   If Windows asks whether to allow Python on the network later, choose **Private networks -> Allow**.

### A7. Type the camera login into this window
Use the user name and password printed on the card in your kit. Type them only here, never in chat, a file or a message:
```powershell
$env:LOGIWEBCAM_USER = "type-the-user-from-the-card"
$env:LOGIWEBCAM_PASSWORD = "type-the-password-from-the-card"
```
They live only in this PowerShell window. If you close it, repeat this step.

### A8. Check the whole link
```powershell
uv run python ..\scripts\check_camera.py
```
Every line should say `[PASS]`. One `[WARN]` about "AI results not updating" is normal here. If a line says `[FAIL]` it prints the fix; do that and run it again.

### A9. See yourself with the camera's face mesh (find the right camera)
```powershell
uv run python vendor\logiinsight\rgb_ai_live.py --face-mesh
```
- It lists the cameras it found with numbers. Pick the one that shows a **colour** picture. The infrared one is grey and tall; your laptop's own webcam has no mesh.
- A window opens with a **green dot mesh on your face**. Move around a little, then press `q` to quit.
- Write down the camera number you picked (call it `N`).
- If the mesh never appears: check A5-A8 again (the AI only runs while the colour camera is open).

### A10. Run the blink test (60 seconds)
Sit in front of the camera, face the screen, blink normally. Replace `N` with your camera number:
```powershell
uv run python src\brain\probes\blink_probe.py --device N --seconds 60
```
Expected: a line for each blink, for example `BLINK #3  closed  120 ms   EAR=0.21`, then every 5 seconds a status line, and at the end a summary like
`Summary: 14 blinks in 60s = 14.0/min | AI results 1500 (25.0/s) | mesh missing in 40 results`.
- Normal is about 15-20 blinks a minute. Count your real blinks for 20 seconds and compare.
- If it prints `(trigger not delivered ...)` after each blink, that is fine for Part A (the plugin is not installed yet).
- At the end the camera AI is switched off on purpose, so the camera can cool down.

**Part A is done.** Send back the whole text from the A10 run (copy it from the window), and tell us how many blinks you counted yourself.
Don't run the camera AI for more than about 10 minutes in a row (it can overheat and freeze; unplug it to cool down).

## Part B - a blink makes the mouse tap (needs the MX Master 4)

### B1. Make sure the plugin folder exists and build the plugin
```powershell
cd $HOME\Documents\hackathon-2026\plugin\WorkHealthCoachPlugin\src
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\Logi\LogiPluginService\Plugins" | Out-Null
dotnet build -c Debug
```
The last line should say `Build succeeded`. (It also creates a link so Logi Options+ loads the plugin from our folder.)

### B2. Restart the Logi plugin service
Open **Logi Options+** -> **Settings** (or the gear/your profile) -> **Restart Logi Plugin Service**. Wait 10 seconds.

### B3. Test the plugin without the camera
Go back to the `brain` folder and send a test trigger:
```powershell
cd $HOME\Documents\hackathon-2026\brain
uv run python ..\scripts\trigger.py --list
uv run python ..\scripts\trigger.py pace_too_fast --force
```
`--list` prints the trigger names; the second command should make the MX Master 4 give a long calm vibration.
No answer ("Cannot reach the plugin")? Repeat B2, and check that the MX Master 4 is connected in Options+.

### B4. Blink -> tap
(Do A7 again if you opened a new PowerShell window.)
```powershell
uv run python src\brain\probes\blink_probe.py --device N --seconds 60
```
Each detected blink should now give **one soft tap** on the mouse. Tell us whether taps and blinks match, and roughly how late the tap feels.

## "Cannot reach the plugin on 127.0.0.1:8787" (Part B, step B3)
This means the plugin is not running or not loaded. Check in this order (PowerShell):

**1. Get the latest code and rebuild** (a Windows-specific server bug was fixed on 2026-11-21):
```powershell
cd C:\LauzHack\hackathon-2026     # your project folder
git pull
cd plugin\WorkHealthCoachPlugin\src
dotnet build -c Debug
```
Then in Logi Options+ -> Settings -> **Restart Logi Plugin Service**, wait 15 seconds, and try `uv run python ..\scripts\trigger.py --list` again.

**2. Is the plugin linked?** This should print a folder path ending in `bin\Debug`:
```powershell
Get-Content "$env:LOCALAPPDATA\Logi\LogiPluginService\Plugins\WorkHealthCoachPlugin.link"
```
"Cannot find path" = the build did not create the link: make the folder (B1) and run `dotnet build -c Debug` again.

**3. What does the plugin say?** (the most useful check):
```powershell
Get-Content "$env:LOCALAPPDATA\Logi\LogiPluginService\Logs\plugin_logs\WorkHealthCoach.log" -Tail 30
```
- `LocalServer listening on http://127.0.0.1:8787/` = OK, the plugin runs.
- `LocalServer could not start ...` = port 8787 is used by another program: `netstat -ano | findstr 8787`.
- No `WorkHealthCoach.log` file at all = the Plugin Service never loaded it. Usually Logi Options+ is too old: the plugin needs
  **Logi Plugin Service 6.4 or newer (.NET 10)**. Update Logi Options+ (Settings -> About / check for updates) and restart the service.
  Check the version: `(Get-Item "C:\Program Files\Logi\LogiPluginService\PluginApi.dll").VersionInfo.FileVersion`

**4. Is anything listening?**
```powershell
netstat -ano | findstr 8787
```
A line with `LISTENING` = the server is up (then it is a firewall/VPN problem: try `Invoke-RestMethod http://127.0.0.1:8787/health`).

Send back the output of steps 2 and 3 if it still fails.

## If something goes wrong
| Problem | What to do |
|---|---|
| `winget` not found | Install "App Installer" in the Microsoft Store, or use the download links in A2 |
| `uv`/`git`/`dotnet` not found | Close PowerShell, open a new one |
| No new adapter in `ncpa.cpl` | `devmgmt.msc` -> CDC NCM -> driver *UsbNcm Host Device* (A6); another USB 3 port/cable |
| `ping 192.168.1.10` fails | Re-check IP `192.168.1.20` / mask `255.255.255.0` / empty gateway; turn Wi-Fi off; disconnect any VPN |
| `Authentication failed` | Re-type the card values in A7 exactly (no extra spaces) |
| Wrong camera opens / no colour picture | Use the number from A9 with `--device N`; try 0, 1, 2 |
| Mesh never shows, `distinct AI results/s: 0.0` | The colour camera isn't being read: redo A5, A9; check Windows camera privacy |
| Video freezes / camera hot | Unplug it, let it cool a few minutes, plug in again; run shorter tests |
| Python asks for network access | Allow on **Private** networks |

---
## Notes from the first test on the Mac (2026-11-21)
- Network adapter, SSH login and firmware checks all passed on the Mac (`scripts/check_camera.py`).
- macOS lists only the infrared camera ("LVNext CPD Camera"); the colour "LVNext Camera" is not offered, so the face AI gave 0 results and no blinks.
  The microphone ("LVNext Microphone") works on the Mac. Hence this Windows test.
