# Work-Health Coach (Logitech hackathon 2026)

Camera + microphone triggers -> feedback on the **MX Master 4** (haptics), the **MX Keypad** (pixel-art screens) and the screen (pixel owl/snail).
Read `docs/HANDOFF.md` first: status, architecture, gotchas, next steps. Other docs are listed in `README.md`.

## Architecture in five lines
1. **C# Logi plugin** (`plugin/WorkHealthCoachPlugin/src`, .NET 10) runs inside Logi Options+ / Logi Plugin Service. It owns ALL outputs and rules.
2. It listens on **`http://127.0.0.1:8787`** (plain `TcpListener`, localhost only). `POST /trigger {"trigger":"blink_low","severity":0.7}`, or `GET /pace/true`, `/signal/<trigger>/true|false`. Contract: `docs/TRIGGER_CONTRACT.md`.
3. `NotificationPolicy.cs` decides what reaches the user (severity, escalation, cooldown, budget, snooze, back-off). Detection code never implements cooldowns.
4. **Python** (`brain/`, run with `uv`, Python 3.12) detects things and sends triggers: `monitors/blink_monitor.py` (camera), `monitors/pace_monitor.py` (mic, teammate's algorithm), launcher `app.py` runs both plus `overlay/overlay.py` (PySide6 window) in ONE process.
5. Sprites: `scripts/gen_sprites.py` -> `assets/sprites.json` (overlay) + `plugin/.../Ui/SpriteData.g.cs` (Keypad).

## Run
```
cd plugin/WorkHealthCoachPlugin/src && dotnet build -c Debug     # links the plugin into Options+ (also reloads it)
cd brain && uv sync
uv run python src/brain/app.py --simulate --no-blink-after 5      # no hardware: fake blinks + fake talker, 50 s
uv run python src/brain/app.py --list                              # camera + microphone numbers
uv run python src/brain/app.py --device N --mic M --force          # real
python scripts/trigger.py blink_low --force                        # fake a trigger (--force skips the policy)
```
Windows details: `docs/WINDOWS_DEMO.md`. macOS plugin reload: `open "loupedeck:plugin/WorkHealthCoach/reload"`.

## Rules for working here
- **Never commit or paste the camera login** (card in the kit). It lives in env vars `LOGIWEBCAM_USER/PASSWORD` or `~/.config/workhealth/camera.env`.
- Trigger names are fixed in `HapticPlayer.cs` (patterns) + `Triggers` class in `LocalServer.cs`; new alert = pattern + rule in `PolicySettings.cs` + Keypad/overlay reaction.
- A trigger that "does nothing" is usually the policy: read the plugin log (`.../Logi/LogiPluginService/Logs/plugin_logs/WorkHealthCoach.log`), use `--force` to test the drawing.
- `GET /health` returns the plugin build time (UTC). If it is old after `dotnet build`, the old plugin is still running (Windows locks the DLL): close Options+ fully, rebuild, restart.
- Keep changes small and tested with `--simulate` first; real-hardware results go in `docs/`.
- Camera AI must not run > ~10 min continuously (overheats). One face per result only.
