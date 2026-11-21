# Phase 0 - Setup (do these in order)

Each step says **what**, **why**, **how**, and what you should **see**. Run commands in the Terminal app.

## 0.1 Check Options+ sees your devices
- **What:** open Logi Options+ and confirm the **MX Master 4** and **MX Keypad** appear.
- **Why:** the plugin can only buzz and draw on devices Options+ manages. Haptics work on the MX Master 4 only.
- **See:** both devices listed. If your mouse is another model, tell Claude - haptics will not work and we use a fallback.

## 0.2 Install `uv` (Python tool manager)
- **Why:** the Mac has Python 3.14, which MediaPipe/OpenCV don't support yet. `uv` gives us a clean Python 3.12.
```bash
brew install uv
```
- **See:** `uv --version` prints a version.

## 0.3 Install the .NET 10 SDK
- **Why:** Logitech plugins are C# (.NET 10). The Logi Plugin Service loads what we build.
```bash
brew install dotnet@10
```
(The `dotnet-sdk` cask is .NET 11 and needs an admin password; `dotnet@10` is the version Logitech's samples target.)
- Make it findable from every terminal:
```bash
echo 'export DOTNET_ROOT="/opt/homebrew/opt/dotnet@10/libexec"; export PATH="/opt/homebrew/opt/dotnet@10/bin:$PATH"' >> ~/.zshrc
```
- **See:** `dotnet --version` prints `10.0.x` (open a new terminal first).

## 0.4 Install the Logi plugin tool
- **Why:** generates the plugin skeleton and packages it.
```bash
dotnet tool install --global LogiPluginTool
```
- Then add dotnet tools to PATH if `logiplugintool --help` isn't found:
```bash
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.zshrc && source ~/.zshrc
```
- **See:** `logiplugintool --help` prints help.

## 0.5 Tell Claude when 0.1-0.4 are done
Claude then generates the plugin in `plugin/` and gives you Phase 1 ("Hello buzz": press a Keypad button, the mouse buzzes).

## Later (only when the teammate hands over the webcam kit)
- Never paste the kit card's user name/password into chat or files. Set them only in your terminal:
  `export LOGIWEBCAM_USER='...'` and `export LOGIWEBCAM_PASSWORD='...'`
- Turn **Wi-Fi off** (or use a phone hotspot) while testing the camera link; give the camera's network adapter IP `192.168.1.20`, mask `255.255.255.0`, no router.
- Allow **Camera**, **Microphone** and **Local Network** for Terminal in System Settings -> Privacy & Security.
