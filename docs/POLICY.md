# Notification policy (how the coach stays un-annoying)

Raw triggers (camera, mic, timers) never touch your mouse or Keypad directly. They pass through
`plugin/.../Policy/NotificationPolicy.cs`, which applies, in order:
1. **Snooze** (everything except critical alerts: `time_warning`, `person_behind`)
2. **Enabled** per trigger
3. **Minimum severity** (weak signals are ignored)
4. **Escalation**: Keypad first; the mouse only buzzes if the problem persists `hapticAfterSec`
5. **Cooldown** between alerts (stretched x2..x4 when you dismissed alerts twice in 10 min)
6. **Budget**: at most `maxPer10Min` alerts per trigger

Presets (Level key / `POST /preset`): **quiet** (high thresholds, long cooldowns, mouse only for pace, critical stays), **balanced** (default), **attentive**.
Mouse strength (Buzz key): off / light (one tap) / normal / strong (pattern twice).

## Keys in Options+ (search "Coach")
- *Snooze alerts (15 min)*, *Notification level*, *Mouse buzz strength* (group "Coach controls")
- Pressing the **Alert** tile dismisses it (counts toward quieter behaviour)

## Settings file and API
File: `settings.json` in `~/Library/Application Support/WorkHealthCoach/` (Windows: `%APPDATA%\WorkHealthCoach\`). Human-readable; per-trigger rules.
```
GET  /settings            POST /settings {full json}      POST /preset {"name":"quiet"}      POST /snooze {"minutes":15}
```
The policy logs every decision (`Policy: pace_too_fast dropped: cooldown (90s)`) in the plugin log, so tuning is easy.

Verified: severity filter, visual-first escalation then mouse, cooldown, quiet/attentive presets, snooze (critical passes).
