using Loupedeck;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Loupedeck.WorkHealthCoachPlugin;

// Test harness: exercises the plugin code that does not need Options+ or a device.
// Safety: settings files are redirected to a temp folder; the run aborts if that did not work.

if (args.Length == 1 && args[0] == "--enums")
{
    foreach (var t in new[] { typeof(VirtualKeyCode), typeof(ModifierKey) })
    {
        Console.WriteLine($"{t.Name}: {String.Join(" ", Enum.GetNames(t))}");
    }

    return 0;
}

// --render <folder>: draws the Keypad screens (whole 3x3 picture) to PNG files for a visual check. Touches no settings.
if (args.Length == 2 && args[0] == "--render")
{
    Directory.CreateDirectory(args[1]);
    void Save(String name, PixelScene scene) => Console.WriteLine($"{name}: {scene.Whole(348).SaveToFile(Path.Combine(args[1], name + ".png"))}");

    PomodoroState.Configure(25, 5, 15, 4, false, true, 5);
    PomodoroState.Start();
    Save("pomodoro_focus", KeypadScreen.Build());
    PomodoroState.TogglePause();
    Save("pomodoro_paused", KeypadScreen.Build());
    PomodoroState.TogglePause();
    PomodoroState.SetPresence(false);
    Save("pomodoro_away", KeypadScreen.Build());
    PomodoroState.SetPresence(true);
    PomodoroState.Skip();
    PomodoroState.TogglePause();
    Save("pomodoro_break", KeypadScreen.Build());
    PomodoroState.Stop();
    Save("idle_presentation", KeypadScreen.Build());
    SessionState.OnTrigger(new TriggerMessage("break_due", 1.0));
    Save("reminder_break_due", KeypadScreen.Build());
    SessionState.OnTrigger(new TriggerMessage("speak_up", 1.0), new TriggerRule { KeypadColor = "#3da5ff" });
    Save("reminder_speak_up", KeypadScreen.Build());
    SessionState.OnTrigger(new TriggerMessage("break_over", 1.0));
    Save("reminder_break_over", KeypadScreen.Build());
    // one Desk Coach widget key while an alert is active (every widget key draws this)
    void SaveKey(String name)
    {
        var img = KeypadScreen.AlertKey(116);
        Console.WriteLine($"{name}: {(img == null ? "NO ALERT" : img.SaveToFile(Path.Combine(args[1], name + ".png")))}");
    }

    SessionState.DismissAlert();
    foreach (var (t, n) in new[] { ("person_behind", "key_behind"), ("break_due", "key_break_due"), ("speak_up", "key_speak_up"), ("break_over", "key_break_over"),
                                   ("blink_low", "key_blink"), ("pace_too_fast", "key_pace"), ("time_warning", "key_time") })
    {
        SessionState.OnTrigger(new TriggerMessage(t, 1.0));
        SaveKey(n);
        SessionState.DismissAlert();
    }

    return 0;
}

// run.sh sets WORKHEALTH_DATA_DIR to a throw-away folder; this check refuses to run against the real settings.
var tmp = Environment.GetEnvironmentVariable("WORKHEALTH_DATA_DIR") ?? String.Empty;
if (!tmp.Contains("workhealth-harness") || !PolicySettings.FilePath.StartsWith(tmp))
{
    Console.WriteLine($"ABORT: settings would be written to {PolicySettings.FilePath}, not a harness temp folder. Run tests/Harness/run.sh. Nothing was touched.");
    return 2;
}

if (args.Length >= 1 && args[0] == "--serve")
{
    // Runs the real server + policy + Pomodoro timer for browser tests of the settings page (no Options+, no devices).
    NotificationPolicy.Init();
    UiSettings.Init();
    NotificationPolicy.Decided += d => Console.WriteLine($"decision: {d.Message.Trigger} haptic={d.Haptic} keypad={d.Visual} screen={d.Overlay} pattern={d.Rule?.HapticPattern}");
    TriggerBus.Received += m => { if (m.Trigger is not ("blink_detected" or "pace_ok")) { NotificationPolicy.Evaluate(m); } };
    FarVolume.Audio = new FakeAudio { Volume = 40, Log = true };   // never the real volume
    var srv = new LocalServer();
    srv.Start();
    var ticker = new System.Timers.Timer(1000) { AutoReset = true };
    ticker.Elapsed += (_, _) => { SessionState.Tick(); PomodoroState.Tick(); FarVolume.Tick(); PersonBehind.Tick(); };
    ticker.Start();
    Console.WriteLine($"serving http://127.0.0.1:{LocalServer.Port}/  (data in {tmp})");
    await Task.Delay(TimeSpan.FromSeconds(args.Length > 1 ? Int32.Parse(args[1]) : 600));
    srv.Stop();
    return 0;
}

// The real plugin (Options+) listens on 8787. The harness must never talk to it, so it needs its own port.
if (LocalServer.Port == 8787)
{
    Console.WriteLine("ABORT: the harness would use port 8787, which belongs to the real plugin. Run tests/Harness/run.sh (sets WORKHEALTH_PORT). Nothing was touched.");
    return 2;
}

try
{
    using var probe = new System.Net.Sockets.TcpClient();
    await probe.ConnectAsync("127.0.0.1", LocalServer.Port).WaitAsync(TimeSpan.FromSeconds(1));
    Console.WriteLine($"ABORT: something already listens on port {LocalServer.Port}. Nothing was touched.");
    return 2;
}
catch (Exception ex) when (ex is not OperationCanceledException || true)
{
    // nothing listening: good
}

var pass = 0;
var fail = 0;
void Check(String name, Boolean ok, String detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  ok    {name}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name} {detail}"); }
}

var decisions = new List<Decision>();
NotificationPolicy.Init();
UiSettings.Init();
NotificationPolicy.Decided += decisions.Add;
TriggerBus.Received += m =>
{
    if (m.Trigger is "blink_detected" or "pace_ok") { return; }
    NotificationPolicy.Evaluate(m);
};

Console.WriteLine("Static files (embedded settings page)");
Check("embedded files found", StaticFiles.Count > 0, $"count={StaticFiles.Count}");
Check("GET / is the page", StaticFiles.TryGet("", out var idx, out var idxType) && idxType.StartsWith("text/html") && Encoding.UTF8.GetString(idx).Contains("Desk Coach"));
Check("icon png", StaticFiles.TryGet("/assets/icons/speed.png", out _, out var t1) && t1 == "image/png");
Check("sound wav", StaticFiles.TryGet("/assets/sounds/chime.wav", out _, out var t2) && t2 == "audio/wav");
Check("no path traversal", !StaticFiles.TryGet("/../secret", out _, out _));

Console.WriteLine("Local server");
var server = new LocalServer();
server.Start();
await Task.Delay(400);
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{LocalServer.Port}") };
async Task<(Int32 code, String body, String type)> Send(String method, String path, String body = null)
{
    var req = new HttpRequestMessage(new HttpMethod(method), path);
    if (body != null) { req.Content = new StringContent(body, Encoding.UTF8, "application/json"); }
    var r = await http.SendAsync(req);
    return ((Int32)r.StatusCode, await r.Content.ReadAsStringAsync(), r.Content.Headers.ContentType?.ToString() ?? "");
}

var home = await Send("GET", "/");
Check("GET / serves HTML", home.code == 200 && home.type.StartsWith("text/html") && home.body.Contains("Desk Coach"), $"{home.code} {home.type}");
Check("GET /assets/icons/behind.png", (await Send("GET", "/assets/icons/behind.png")).code == 200);
Check("GET /health still JSON", (await Send("GET", "/health")) is { code: 200 } h && h.body.Contains("\"ok\":true"));
Check("unknown path 404", (await Send("GET", "/nope")).code == 404);
Check("new triggers listed", (await Send("GET", "/triggers")).body is var tr && new[] { "speak_up", "speak_down", "break_due", "break_over", "volume_far" }.All(tr.Contains));

Console.WriteLine("Settings page state -> policy");
const String state = """
{"master":true,"profile":"everyday","muted":[],"features":{
 "speed":{"enabled":true,"cooldown":30,"ch":{"audio":false,"screen":true,"haptic":true,"keypad":false},"fb":{"haptic":{"pattern":"bump","repeat":2},"keypad":{"mode":"color","color":"#ff0000"},"screen":{"style":"banner","texts":{"text":"Slow down","text2":"Pick up the pace"}}}},
 "blink":{"enabled":true,"cooldown":1200,"ch":{"screen":true,"haptic":false,"keypad":true}},
 "behind":{"enabled":true,"cooldown":60,"ch":{"screen":true,"haptic":true,"keypad":true}},
 "volume":{"enabled":true,"cooldown":30,"ch":{"screen":true,"haptic":false,"keypad":true},"fb":{"keypad":{"mode":"color","color":"#3da5ff"}}},
 "pomodoro":{"enabled":false,"cooldown":180,"ch":{"screen":true,"haptic":true,"keypad":true},"params":{"work":1,"short":1,"long":1,"rounds":2,"auto":false,"cam":true,"reset":5}}}}
""";
var post = await Send("POST", "/ui-state", state);
Check("POST /ui-state accepted", post.code == 200 && post.body.Contains("\"ok\":true"), post.body);
Check("bad state rejected", (await Send("POST", "/ui-state", "{\"nope\":1}")).code == 400);
Check("not json rejected", (await Send("POST", "/ui-state", "hello")).code == 400);
var rules = NotificationPolicy.Settings.Triggers;
Check("speed cooldown 30 s", rules["pace_too_fast"].CooldownSec == 30, $"{rules["pace_too_fast"].CooldownSec}");
Check("speed haptic pattern bump x2", rules["pace_too_fast"].HapticPattern == "bump" && rules["pace_too_fast"].HapticRepeat == 2);
Check("speed keypad colour", rules["pace_too_fast"].KeypadColor == "#ff0000");
Check("speed channels: screen+haptic, no keypad", rules["pace_too_fast"].Overlay && rules["pace_too_fast"].Haptic && !rules["pace_too_fast"].Visual);
Check("blink cooldown 20 min, no haptic", rules["blink_low"].CooldownSec == 1200 && !rules["blink_low"].Haptic);
Check("behind cooldown 60 s", rules["person_behind"].CooldownSec == 60);
Check("volume maps to speak_up + speak_down", rules["speak_up"].CooldownSec == 30 && rules["speak_down"].Visual);
Check("state echoed by GET /ui-state", (await Send("GET", "/ui-state")).body.Contains("\"cooldown\":1200"));
Check("state file written", File.Exists(UiSettings.FilePath));

Console.WriteLine("Triggers through the policy");
decisions.Clear();
await Send("POST", "/trigger", "{\"trigger\":\"person_behind\",\"severity\":0.9}");
Check("person_behind delivered", decisions.Count == 1 && decisions[0].Visual && decisions[0].Haptic, $"n={decisions.Count}");
await Send("POST", "/trigger", "{\"trigger\":\"person_behind\",\"severity\":0.9}");
Check("repeat blocked by 60 s cooldown", decisions.Count == 1);
await Send("POST", "/trigger", "{\"trigger\":\"speak_up\",\"severity\":0.8}");
Check("speak_up delivered with rule", decisions.Count == 2 && decisions[1].Rule?.KeypadColor == "#3da5ff");
await Send("POST", "/trigger", "{\"trigger\":\"speak_up\",\"force\":true,\"only\":\"keypad\"}");
Check("forced + only=keypad", decisions.Count == 3 && decisions[2].Visual && !decisions[2].Haptic && !decisions[2].Overlay);
await Send("POST", "/trigger", "{\"trigger\":\"speak_up\",\"force\":true,\"only\":\"haptic\"}");
Check("forced + only=haptic", decisions[3].Haptic && !decisions[3].Visual && !decisions[3].Overlay);
Check("unknown trigger 400", (await Send("POST", "/trigger", "{\"trigger\":\"nope\"}")).code == 400);
Check("signal true/false endpoint", (await Send("GET", "/signal/blink_low/true?force=1")).code == 200 && decisions.Count == 5);

Console.WriteLine("Master switch and muted channels");
decisions.Clear();
UiSettings.SetMaster(false);
await Send("POST", "/trigger", "{\"trigger\":\"blink_low\",\"severity\":1}");
Check("master off drops everything", decisions.Count == 0 && !UiSettings.Master);
UiSettings.SetMaster(true);
UiSettings.ApplyProfile("presenting");
Check("presenting profile enables speed, volume, behind only", UiSettings.FeatureEnabled("speed") && !UiSettings.FeatureEnabled("blink") && NotificationPolicy.Settings.Muted.SequenceEqual(new[] { "audio", "screen" }));
decisions.Clear();
await Send("POST", "/trigger", "{\"trigger\":\"pace_too_fast\",\"severity\":1}");
await Send("POST", "/trigger", "{\"trigger\":\"speak_up\",\"severity\":1}");
Check("presenting mutes the on-screen channel", decisions.Count >= 1 && decisions.All(d => !d.Overlay && !d.Audio), $"n={decisions.Count}");
UiSettings.TogglePresenting();
Check("toggle back to everyday", UiSettings.Profile == "everyday" && NotificationPolicy.Settings.Muted.Count == 0);
UiSettings.SetFeatureEnabled("blink", false);
Check("keypad toggle changes the policy", !NotificationPolicy.Settings.Triggers["blink_low"].Enabled && UiSettings.Profile == "custom");
Check("keypad toggle bumps rev for the page", JsonDocument.Parse(UiSettings.ToJson()).RootElement.GetProperty("rev").GetInt64() > 1);

Console.WriteLine("Haptic choices");
var bump2 = HapticPlayer.Resolve("pace_too_fast", "normal", "bump", 2);
Check("bump x2 = two pulses, 300 ms gap", bump2.Length == 2 && bump2[0].Pulse == HapticPlayer.Bump && bump2[0].DelayAfterMs == 300 && bump2[1].DelayAfterMs == 0);
Check("strong repeats the pattern", HapticPlayer.Resolve("x", "strong", "tap", 1).Length == 2);
Check("off = silent", HapticPlayer.Resolve("pace_too_fast", "off", "jingle", 1).Length == 0);
Check("no choice = built-in pattern (team: break_due soft x3, speak_up crisp + soft)", HapticPlayer.Resolve("break_due", "normal", null, 1) is { Length: 3 } bd && bd[0].Pulse == HapticPlayer.Soft && HapticPlayer.Resolve("speak_up", "normal", null, 1)[0].Pulse == HapticPlayer.Sharp);
Check("every trigger has a pattern", new[] { "speak_up", "speak_down", "break_due", "break_over", "volume_far" }.All(n => HapticPlayer.Resolve(n, "normal", null, 1) is { Length: > 0 }));
Check("four choices", HapticPlayer.ChoiceNames.OrderBy(x => x).SequenceEqual(new[] { "alert", "bump", "jingle", "tap" }));

Console.WriteLine("Pomodoro");
var clock = DateTime.UtcNow;
PomodoroState.Now = () => clock;
decisions.Clear();
Check("enabling the feature starts the timer", await EnablePomodoro(true) && PomodoroState.Active && PomodoroState.Running && PomodoroState.LeftSec == 60, $"left={PomodoroState.LeftSec}");
PomodoroState.Tick(30);
Check("counts down at the desk", PomodoroState.LeftSec == 30);
await Send("GET", "/signal/presence/false");
PomodoroState.Tick(10);
Check("paused while away", PomodoroState.LeftSec == 30 && !PomodoroState.Counting);
clock = clock.AddMinutes(5).AddSeconds(2);
PomodoroState.Tick(1);
Check("away 5+ min resets the timer", PomodoroState.LeftSec == 60 && PomodoroState.Current == PomodoroState.Phase.Work, $"left={PomodoroState.LeftSec}");
await Send("POST", "/presence", "{\"atDesk\":true}");
Check("back at the desk counts again", PomodoroState.Counting);
PomodoroState.Tick(60);
Check("focus end -> short break + break_due", PomodoroState.Current == PomodoroState.Phase.Short && decisions.Any(d => d.Message.Trigger == "break_due"), $"phase={PomodoroState.PhaseName}");
Check("timer stops at the break (auto off)", !PomodoroState.Running);
PomodoroState.TogglePause();
await Send("GET", "/signal/presence/false");
PomodoroState.Tick(30);
Check("breaks run while away", PomodoroState.LeftSec == 30);
await Send("GET", "/signal/presence/true");
PomodoroState.Tick(30);
Check("break end -> focus 2 + break_over", PomodoroState.Current == PomodoroState.Phase.Work && PomodoroState.Round == 2 && decisions.Any(d => d.Message.Trigger == "break_over"));
PomodoroState.TogglePause();
PomodoroState.Tick(60);
Check("last round -> long break", PomodoroState.Current == PomodoroState.Phase.Long && PomodoroState.Round == 1);
PomodoroState.Skip();
Check("skip -> focus", PomodoroState.Current == PomodoroState.Phase.Work);
var snap = JsonDocument.Parse((await Send("GET", "/pomodoro")).body).RootElement;
Check("GET /pomodoro snapshot", snap.GetProperty("active").GetBoolean() && snap.GetProperty("phase").GetString() == "work" && snap.GetProperty("total").GetInt32() == 60);
Check("POST /pomodoro/reset", (await Send("POST", "/pomodoro/reset")).code == 200 && PomodoroState.LeftSec == 60);
Check("bad pomodoro action 404", (await Send("POST", "/pomodoro/boom")).code == 404);
await Send("POST", "/pomodoro/stop");
Check("stop switches the feature off", !PomodoroState.Active && !UiSettings.FeatureEnabled("pomodoro"));
await Send("POST", "/pomodoro/start");
Check("start switches the feature on", PomodoroState.Active && UiSettings.FeatureEnabled("pomodoro"));

Console.WriteLine("Volume when far (fake sound device + fake clock, no camera)");
var audio = new FakeAudio { Volume = 40 };
var fclock = DateTime.UtcNow;
var media = 0;
var farTriggers = 0;
FarVolume.Audio = audio;
FarVolume.Now = () => fclock;
FarVolume.SendPlayPause = () => media++;
TriggerBus.Received += m => { if (m.Trigger == "volume_far") { farTriggers++; } };
void Step(Int32 sec) { for (var i = 0; i < sec; i++) { fclock = fclock.AddSeconds(1); FarVolume.Tick(1); } }
void Hold(Double ratio, Int32 sec) { for (var i = 0; i < sec; i++) { FarVolume.SetRatio(ratio); Step(1); } }
void Cfg(Boolean smooth = false, Boolean restore = true, Int32 leave = 120, Boolean resume = true, Boolean key = false, Int32 cap = 90, Int32 boost = 20) =>
    FarVolume.Configure(true, 30, 2, boost, cap, smooth, restore, leave, resume, key);

FarVolume.Configure(false, 30, 2, 20, 90, false, true, 120, true, false);
FarVolume.SetPresence(true);
Hold(0.4, 6);
Check("feature off: nothing changes", audio.Volume == 40 && audio.Sets == 0);

Cfg();
Hold(1.0, 3);
Check("near the screen: no change", audio.Volume == 40);
Hold(0.6, 1);
Check("far, but not for 2 s yet: no change", audio.Volume == 40);
Hold(0.6, 2);
Check("far for 2 s: volume 40 -> 60 and one volume_far trigger", audio.Volume == 60 && farTriggers == 1 && FarVolume.Boosted, $"vol={audio.Volume} triggers={farTriggers}");
Hold(1.0, 3);
Check("back near: volume restored to 40", audio.Volume == 40 && !FarVolume.Boosted, $"vol={audio.Volume}");

audio.Volume = 85; farTriggers = 0;
Hold(0.5, 4);
Check("never above the cap (85 + 20 -> 90)", audio.Volume == 90);
Hold(1.0, 4);
Check("restored to 85", audio.Volume == 85);
audio.Volume = 95; farTriggers = 0;
Hold(0.5, 4);
Check("already above the cap: never lowered, no raise", audio.Volume == 95 && farTriggers == 0);
Hold(1.0, 4);

Cfg(smooth: true);
audio.Volume = 40; audio.MaxJump = 0;
Hold(0.5, 3);
Check("smooth: first step only +5", audio.Volume == 45, $"vol={audio.Volume}");
Hold(0.5, 4);
Check("smooth: reaches 60 in small steps", audio.Volume == 60 && audio.MaxJump <= 5, $"vol={audio.Volume} maxJump={audio.MaxJump}");
Hold(1.0, 12);
Check("smooth: restores to 40 in small steps", audio.Volume == 40 && audio.MaxJump <= 5, $"vol={audio.Volume} maxJump={audio.MaxJump}");

Cfg();
audio.Volume = 40;
Hold(0.5, 3);
audio.Volume = 30;   // the user turns it down by hand
Hold(0.5, 4);
Check("user changed the volume: we do not fight them", audio.Volume == 30 && !FarVolume.Boosted, $"vol={audio.Volume}");
Hold(1.0, 3);
Hold(0.5, 3);
Check("later far again: boosts from the new volume", audio.Volume == 50, $"vol={audio.Volume}");
Hold(1.0, 3);
Check("and restores to the user's 30", audio.Volume == 30);

Cfg();
audio.Volume = 40; audio.Muted = false;
FarVolume.SetPresence(false);
Step(3);
Check("out of the frame counts as far: volume raised", audio.Volume == 60, $"vol={audio.Volume}");
Step(100);
Check("away less than 120 s: not muted", audio.Muted == false);
Step(21);
Check("away 120 s: sound muted", audio.Muted == true && FarVolume.Stopped);
FarVolume.SetPresence(true);
Step(4);
Check("back: unmuted and volume restored", audio.Muted == false && audio.Volume == 40 && !FarVolume.Stopped, $"muted={audio.Muted} vol={audio.Volume}");

audio.Muted = true;
FarVolume.SetPresence(false);
Step(125);
FarVolume.SetPresence(true);
Step(4);
Check("user had it muted: stays muted", audio.Muted == true);
audio.Muted = false;

media = 0;
Cfg(leave: 10, key: true);
FarVolume.SetPresence(false);
Step(12);
Check("Play/Pause mode: pressed once after 10 s away", media == 1 && audio.Muted == false);
FarVolume.SetPresence(true);
Step(4);
Check("Play/Pause mode: pressed again on return", media == 2);
Cfg(leave: 10, key: true, resume: false);
FarVolume.SetPresence(false);
Step(12);
FarVolume.SetPresence(true);
Step(4);
Check("resume off: no second press", media == 3);

Cfg();
audio.Volume = 40; audio.Muted = false;
FarVolume.SetPresence(false);
Step(125);
FarVolume.Configure(false, 30, 2, 20, 90, false, true, 120, true, false);
Check("switching the feature off gives sound and volume back", audio.Muted == false && audio.Volume == 40, $"muted={audio.Muted} vol={audio.Volume}");
FarVolume.SetPresence(true);

Cfg();
audio.Volume = null;
Hold(0.4, 6);
var setsBefore = audio.Sets;
Hold(0.4, 6);
Check("no readable volume: no crash, nothing set", audio.Sets == setsBefore);
audio.Volume = 40;
FarVolume.SetPresence(false); FarVolume.SetPresence(true);
Step(12);

Cfg();
audio.Volume = 40;
FarVolume.SetRatio(0.5);
Step(12);
Check("no fresh distance samples: not treated as far for ever", audio.Volume == 40 && !FarVolume.Boosted, $"vol={audio.Volume}");

Console.WriteLine("Volume when far: API and settings page mapping");
Check("calibrate with no data -> 400", (await Send("POST", "/distance/calibrate")).code == 400);
Check("face before calibration -> 400", (await Send("POST", "/distance", "{\"face\":0.09}")).code == 400);
Check("calibrate from the latest face", (await Send("POST", "/distance/calibrate")).code == 200);
Check("face 0.045 vs baseline 0.09 -> ratio 0.5", (await Send("POST", "/distance", "{\"face\":0.045}")).code == 200 && (await Send("GET", "/distance")).body is var gj && gj.Contains("\"ratio\":0.5") && gj.Contains("\"baseline\":0.09"));
Check("ratio accepted", (await Send("POST", "/distance", "{\"ratio\":0.8}")).code == 200);
Check("bad body -> 400", (await Send("POST", "/distance", "{\"nope\":1}")).code == 400);
Check("not json -> 400", (await Send("POST", "/distance", "hello")).code == 400);
Check("GET /distance shows the (fake) system volume", (await Send("GET", "/distance")).body.Contains($"\"volume\":{audio.Volume}"));
await Send("POST", "/presence", "{\"atDesk\":false}");
Check("presence reaches the volume engine", !FarVolume.AtDesk);
await Send("GET", "/signal/presence/true");
Check("signal/presence too", FarVolume.AtDesk);

const String farState = """
{"master":true,"profile":"everyday","muted":[],"features":{"distance":{"enabled":true,"cooldown":0,"params":{"shrink":40,"hold":3,"boost":15,"cap":80,"smooth":false,"restore":true,"leave":60,"resume":true,"leaveAction":"Press Play/Pause"}}}}
""";
Check("state accepted", (await Send("POST", "/ui-state", farState)).code == 200);
Check("page settings reach the engine", FarVolume.Enabled && FarVolume.ShrinkPct == 40 && FarVolume.HoldSec == 3 && FarVolume.BoostPct == 15 && FarVolume.CapPct == 80 && FarVolume.LeaveSec == 60 && FarVolume.UsePlayPause && !FarVolume.Smooth);
UiSettings.SetMaster(false);
Check("master off switches the feature off", !FarVolume.Enabled);
UiSettings.SetMaster(true);
Check("master on switches it back on", FarVolume.Enabled);
UiSettings.SetFeatureEnabled("distance", false);
Check("feature key off", !FarVolume.Enabled);

if (OperatingSystem.IsMacOS())
{
    Console.WriteLine("Volume when far: real macOS volume (read-only; sets the SAME value back, never changes loudness or mute)");
    var real = new MacAudio();
    var rv = real.GetVolume();
    Check("reads the system volume", rv is >= 0 and <= 100, $"{rv}");
    Check("reads the mute state", real.GetMuted() != null);
    Check("setting the same volume works and changes nothing", rv.HasValue && real.SetVolume(rv.Value) && real.GetVolume() == rv);
}

FarVolume.Audio = new NoAudio();

Console.WriteLine("Person behind (simulated walkers, fake clock, no camera)");
FarVolume.Audio = audio;
var bclock = DateTime.UtcNow;
PersonBehind.Now = () => bclock;
var behindAlerts = new List<Double>();
TriggerBus.Received += m => { if (m.Trigger == "person_behind") { behindAlerts.Add(m.Severity); } };
var shortcuts = new List<(VirtualKeyCode Key, ModifierKey Mods)>();
var playPause = 0;
BehindResponse.SendShortcut = (k, m) => shortcuts.Add((k, m));
BehindResponse.SendPlayPause = () => playPause++;
var cmdKey = OperatingSystem.IsMacOS() ? ModifierKey.Command : ModifierKey.Windows;

// 8 samples a second, the engine ticks once a second
void Feed(Int32 id, Double seconds, Func<Double, (Double X, Double Size)> at, Int32? id2 = null, Func<Double, (Double X, Double Size)> at2 = null)
{
    var i = 0;
    for (var t = 0.0; t < seconds - 1e-9; t += 0.125)
    {
        bclock = bclock.AddSeconds(0.125);
        var a = at(t);
        PersonBehind.AddSample(id, a.X, a.Size);
        if (id2.HasValue) { var b = at2(t); PersonBehind.AddSample(id2.Value, b.X, b.Size); }
        if (++i % 8 == 0) { PersonBehind.Tick(); }
    }
}
void Quiet(Int32 seconds) { for (var i = 0; i < seconds; i++) { bclock = bclock.AddSeconds(1); PersonBehind.Tick(); } }
void ResetBehind() { PersonBehind.Clear(); Quiet(5); behindAlerts.Clear(); shortcuts.Clear(); }
(Double X, Double Size) Pass(Double t) => (0.0 + 0.3 * t, 0.06);          // crosses the picture sideways, about 5 face widths a second
(Double X, Double Size) Come(Double t) => (0.5, 0.03 + 0.02 * t);          // straight towards the camera, face grows
(Double X, Double Size) Stand(Double t) => (0.55 + 0.01 * Math.Sin(t), 0.07);   // stands behind the chair

PersonBehind.Configure(true, 2, 3, true, 3);
BehindResponse.Configure("notify", "⌘ ⌥ H", "", 0, 30);
Quiet(3);
Check("nobody there: no alert", behindAlerts.Count == 0);
Feed(1, 3, Pass); Quiet(4);
Check("someone walks past: NO alert", behindAlerts.Count == 0, $"alerts={behindAlerts.Count}");
ResetBehind();
Feed(2, 4, Come);
Check("someone comes closer: alert (severity 0.9)", behindAlerts.Count == 1 && behindAlerts[0] == 0.9 && (PersonBehind.LastReason ?? "").StartsWith("coming"), $"alerts={behindAlerts.Count} reason={PersonBehind.LastReason}");
Feed(2, 3, _ => (0.5, 0.11));
Check("one alert per visit", behindAlerts.Count == 1);
ResetBehind();
Feed(3, 2.6, Stand);
Check("standing behind, not long enough yet: no alert", behindAlerts.Count == 0);
Feed(3, 1.5, t => Stand(t + 2.6));
Check("stays behind for 3 s: alert (severity 0.7)", behindAlerts.Count == 1 && behindAlerts[0] == 0.7 && (PersonBehind.LastReason ?? "").StartsWith("stays"), $"alerts={behindAlerts.Count} reason={PersonBehind.LastReason}");
ResetBehind();
Feed(4, 5, t => (0.5, 0.01 + 0.002 * t));
Check("too far away (beyond 3 m): no alert", behindAlerts.Count == 0);
ResetBehind();
Feed(5, 2.2, t => (0.5, 0.03 + 0.003 * t));
Check("a visitor who is seen briefly and leaves: no alert", behindAlerts.Count == 0);
Quiet(4);

ResetBehind();
Feed(7, 4, Pass, 8, Come);
var tracks = JsonDocument.Parse(PersonBehind.ToJson()).RootElement.GetProperty("tracks").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetInt32(), e => e.GetProperty("alerted").GetBoolean());
Check("two people: only the one coming alerts", behindAlerts.Count == 1 && tracks.GetValueOrDefault(8) && !tracks.GetValueOrDefault(7), $"alerts={behindAlerts.Count} tracks={String.Join(",", tracks)}");

ResetBehind();
PersonBehind.Configure(true, 2, 3, false, 3);
Feed(6, 3.2, Pass);
Check("pass-by filter off: any face for 2 s alerts (team table rule)", behindAlerts.Count == 1 && behindAlerts[0] == 0.6, $"alerts={behindAlerts.Count}");
PersonBehind.Configure(true, 2, 3, true, 3);

ResetBehind();
Feed(9, 3.5, Come);
Check("visit alerts", behindAlerts.Count == 1);
ResetBehind();
PersonBehind.Acknowledge();
Feed(10, 4, Come);
Check("after 'it is fine': the next visitor within a minute is ignored", behindAlerts.Count == 0);
bclock = bclock.AddSeconds(61); Quiet(3);
Feed(11, 4, Come);
Check("after a minute: alerts again", behindAlerts.Count == 1);

Console.WriteLine("Person behind: responses (what the Actions Ring buttons do)");
ResetBehind();
BehindResponse.Configure("hide", "⌘ ⌥ H", "⌘ D", 5, 30);
Feed(12, 4, Come);
Check("automatic hide presses the user's shortcut", shortcuts.Count == 1 && shortcuts[0] == (VirtualKeyCode.KeyH, cmdKey | ModifierKey.Alt) && BehindResponse.Hidden, $"{String.Join(",", shortcuts)}");
Quiet(3);
Check("bring-back waits until the person has been gone for 5 s", BehindResponse.Hidden && shortcuts.Count == 1);
Quiet(4);
Check("then presses the bring-back shortcut", !BehindResponse.Hidden && shortcuts.Count == 2 && shortcuts[1] == (VirtualKeyCode.KeyD, cmdKey), $"{String.Join(",", shortcuts)}");
ResetBehind();
BehindResponse.Configure("lock", "⌘ ⌥ H", "", 0, 30);
Feed(13, 4, Come);
Check("automatic lock presses the lock shortcut", shortcuts.Count == 1 && (OperatingSystem.IsMacOS() ? shortcuts[0] == (VirtualKeyCode.KeyQ, ModifierKey.Control | ModifierKey.Command) : shortcuts[0] == (VirtualKeyCode.KeyL, ModifierKey.Windows)));
ResetBehind();
BehindResponse.Configure("notify", "⌘ ⌥ H", "", 0, 30);
Feed(14, 4, Come);
Check("'only notify me': no key pressed", behindAlerts.Count == 1 && shortcuts.Count == 0);

Check("shortcut ⌘ ⌥ H", BehindResponse.TryParseShortcut("⌘ ⌥ H", out var k1, out var m1) && k1 == VirtualKeyCode.KeyH && m1 == (cmdKey | ModifierKey.Alt));
Check("shortcut Ctrl ⇧ F5", BehindResponse.TryParseShortcut("Ctrl ⇧ F5", out var k2, out var m2) && k2 == VirtualKeyCode.F5 && m2 == (ModifierKey.Control | ModifierKey.Shift));
Check("shortcut F11 alone", BehindResponse.TryParseShortcut("F11", out var k3, out var m3) && k3 == VirtualKeyCode.F11 && m3 == ModifierKey.None);
Check("shortcut ⌘ D / ⌘ 5 / ⌘ Enter / Ctrl ArrowLeft", BehindResponse.TryParseShortcut("⌘ D", out var k4, out _) && k4 == VirtualKeyCode.KeyD && BehindResponse.TryParseShortcut("⌘ 5", out var k5, out _) && k5 == VirtualKeyCode.Key5 && BehindResponse.TryParseShortcut("⌘ Enter", out var k6, out _) && k6 == VirtualKeyCode.Return && BehindResponse.TryParseShortcut("Ctrl ArrowLeft", out var k7, out _) && k7 == VirtualKeyCode.ArrowLeft);
Check("bad shortcuts are refused", !BehindResponse.TryParseShortcut("", out _, out _) && !BehindResponse.TryParseShortcut("⌘", out _, out _) && !BehindResponse.TryParseShortcut("A B", out _, out _) && !BehindResponse.TryParseShortcut("⌘ Banana", out _, out _));
shortcuts.Clear();
BehindResponse.Configure("notify", "", "", 0, 30);
Check("hide with no shortcut set: nothing pressed, no crash", !BehindResponse.Hide() && shortcuts.Count == 0);

playPause = 0;
BehindResponse.Run("music"); BehindResponse.Run("music");
Check("music toggle presses Play/Pause and remembers", playPause == 2 && !BehindResponse.MusicPaused);
BehindResponse.Configure("notify", "", "", 0, 30);
audio.Volume = 70;
BehindResponse.Run("talk");
Check("talk mode lowers the volume to 30", audio.Volume == 30 && BehindResponse.Talk);
BehindResponse.Run("talk");
Check("second press restores 70", audio.Volume == 70 && !BehindResponse.Talk);
BehindResponse.Run("talk");
audio.Volume = 50;   // the user turned it themselves
BehindResponse.Run("talk");
Check("user changed the volume meanwhile: left alone", audio.Volume == 50 && !BehindResponse.Talk);
audio.Volume = 20;
BehindResponse.Run("talk");
Check("talk mode never raises the volume", audio.Volume == 20 && BehindResponse.Talk);
BehindResponse.Run("talk");
audio.Volume = null;
BehindResponse.Run("talk");
Check("no readable volume: no crash, no talk mode", !BehindResponse.Talk);
audio.Volume = 40;
Check("snooze action", BehindResponse.Run("snooze") && NotificationPolicy.Snoozed);
NotificationPolicy.Snooze(0);
Check("unknown action is refused", !BehindResponse.Run("nope"));

Console.WriteLine("Person behind: API and settings page mapping");
PersonBehind.Clear();
Check("POST /behind sample", (await Send("POST", "/behind", "{\"id\":1,\"x\":0.5,\"size\":0.07}")).code == 200);
Check("POST /behind samples list", (await Send("POST", "/behind", "{\"samples\":[{\"id\":1,\"x\":0.5,\"size\":0.07},{\"id\":2,\"x\":0.2,\"size\":0.05}]}")).body.Contains("\"samples\":2"));
Check("bad sample -> 400", (await Send("POST", "/behind", "{\"x\":0.5}")).code == 400);
Check("not json -> 400", (await Send("POST", "/behind", "hello")).code == 400);
var gb = await Send("GET", "/behind");
Check("GET /behind shows the tracks", gb.code == 200 && gb.body.Contains("\"tracks\"") && JsonDocument.Parse(gb.body).RootElement.GetProperty("tracks").GetArrayLength() == 2, $"{gb.code} {gb.body}");
Check("POST /behind/clear", (await Send("POST", "/behind/clear")).code == 200 && JsonDocument.Parse((await Send("GET", "/behind")).body).RootElement.GetProperty("tracks").GetArrayLength() == 0);
playPause = 0;
Check("POST /behind/do/music", (await Send("POST", "/behind/do/music")).code == 200 && playPause == 1);
Check("POST /behind/do/ok", (await Send("POST", "/behind/do/ok")).code == 200 && PersonBehind.AckUntil.HasValue);
Check("unknown action -> 404", (await Send("POST", "/behind/do/nope")).code == 404);

const String behindState = """
{"master":true,"profile":"everyday","muted":[],"features":{"behind":{"enabled":true,"cooldown":60,"params":{"hold":3,"range":2,"passby":false,"linger":5,"act":"Lock the screen","sc":"Ctrl ⇧ F5","scBack":"F6","back":7,"duck":25}}}}
""";
Check("state accepted", (await Send("POST", "/ui-state", behindState)).code == 200);
Check("page settings reach the engine", PersonBehind.Enabled && PersonBehind.HoldSec == 3 && PersonBehind.RangeM == 2 && !PersonBehind.FilterPassBy && PersonBehind.LingerSec == 5);
Check("page settings reach the responses", BehindResponse.Act == "lock" && BehindResponse.HideShortcut == "Ctrl ⇧ F5" && BehindResponse.BackShortcut == "F6" && BehindResponse.BackSec == 7 && BehindResponse.DuckPct == 25);
await Send("POST", "/ui-state", behindState.Replace("Lock the screen", "Hide windows (my shortcut)"));
Check("'Hide windows (my shortcut)' -> hide", BehindResponse.Act == "hide");
await Send("POST", "/ui-state", behindState.Replace("Lock the screen", "Blur the screen"));
Check("an unknown or old choice is safe: notify only", BehindResponse.Act == "notify");
UiSettings.SetMaster(false);
Check("master off switches the engine off", !PersonBehind.Enabled);
UiSettings.SetMaster(true);
UiSettings.SetFeatureEnabled("behind", false);
Check("feature off switches the engine off", !PersonBehind.Enabled);
UiSettings.SetFeatureEnabled("behind", true);
FarVolume.Audio = new NoAudio();

Console.WriteLine("MX Keypad keys: pressing one key changes only that key (real key images, no device)");
try
{
    var asm = typeof(FeatureToggleCommand).Assembly;
    asm.GetType("Loupedeck.WorkHealthCoachPlugin.PluginResources")!.GetMethod("Init")!.Invoke(null, new Object[] { asm });
    var keysCmd = new FeatureToggleCommand();
    var getImage = typeof(FeatureToggleCommand).GetMethod("GetCommandImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
    Byte[] KeyImage(String key) => ((BitmapImage)getImage.Invoke(keysCmd, new Object[] { key, PluginImageSize.Width90 }))!.ToArray();
    Dictionary<String, Byte[]> Snapshot() => FeatureToggleCommand.AllKeys.ToDictionary(k => k, KeyImage);
    String Changed(Dictionary<String, Byte[]> a, Dictionary<String, Byte[]> b) => String.Join(",", FeatureToggleCommand.AllKeys.Where(k => !a[k].SequenceEqual(b[k])));

    UiSettings.Set("""{"master":true,"profile":"everyday","muted":[],"features":{"speed":{"enabled":true},"volume":{"enabled":true},"blink":{"enabled":true},"pomodoro":{"enabled":false},"distance":{"enabled":true},"behind":{"enabled":true}}}""", out _);
    var baseline = Snapshot();
    Check("there are 9 keys (8 switches + the Alert light)", FeatureToggleCommand.AllKeys.Length == 9);
    Check("all key images draw", baseline.Values.All(b => b.Length > 100));
    Check("different keys look different", baseline["speed"].SequenceEqual(baseline["volume"]) == false);

    UiSettings.SetFeatureEnabled("blink", false);
    var afterBlink = Snapshot();
    Check("press Eye blinking: ONLY that key changes", Changed(baseline, afterBlink) == "blink", $"changed: {Changed(baseline, afterBlink)}");
    UiSettings.SetFeatureEnabled("blink", true);
    Check("press it again: back to the first picture", Changed(baseline, Snapshot()) == "");

    UiSettings.SetFeatureEnabled("speed", false);
    Check("press Speech speed: only that key changes", Changed(baseline, Snapshot()) == "speed");
    UiSettings.SetFeatureEnabled("speed", true);

    UiSettings.SetMaster(false);
    var masterOff = Snapshot();
    Check("Master off: every feature key and the Master key go grey (by design), Presenting does not", Changed(baseline, masterOff) == "speed,volume,blink,distance,behind,master", $"changed: {Changed(baseline, masterOff)}");
    UiSettings.SetMaster(true);
    Check("Master on: all back", Changed(baseline, Snapshot()) == "");

    UiSettings.TogglePresenting();
    var presenting = Snapshot();
    Check("Presenting on: Eye blinking and Volume-when-far go grey, Presenting lights up, the others stay", Changed(baseline, presenting) == "blink,distance,present", $"changed: {Changed(baseline, presenting)}");
    UiSettings.TogglePresenting();
    Check("Presenting off: keys are back", Changed(baseline, Snapshot()) == "");

    UiSettings.SetFeatureEnabled("behind", false);
    var offKeys = new[] { "speed", "volume", "blink", "distance" };
    foreach (var k in offKeys) { UiSettings.SetFeatureEnabled(k, false); }
    var most = Snapshot();
    Check("each key follows its own switch (5 off, the rest unchanged)", Changed(baseline, most) == "speed,volume,blink,distance,behind", $"changed: {Changed(baseline, most)}");

    // The other direction: flipping a switch on the web page (the page POSTs the whole state) must redraw the keys.
    String PageState(Func<String, Boolean> on) => "{\"master\":true,\"profile\":\"custom\",\"muted\":[],\"features\":{" + String.Join(",", new[] { "speed", "volume", "blink", "pomodoro", "distance", "behind" }.Select(k => $"\"{k}\":{{\"enabled\":{(on(k) ? "true" : "false")}}}")) + "}}";
    await Send("POST", "/ui-state", PageState(k => k != "pomodoro"));
    var pageBase = Snapshot();
    var redraws = 0;
    Action onRedraw = () => redraws++;
    UiSettings.Changed += onRedraw;
    var r1 = await Send("POST", "/ui-state", PageState(k => k != "pomodoro" && k != "volume"));
    Check("page: switch Speech volume off -> plugin accepts it and raises the redraw event", r1.code == 200 && redraws >= 1, $"{r1.code} redraws={redraws}");
    Check("page: ...and ONLY the Speech volume key goes grey", Changed(pageBase, Snapshot()) == "volume", $"changed: {Changed(pageBase, Snapshot())}");
    await Send("POST", "/ui-state", PageState(k => k != "pomodoro"));
    Check("page: switch it on again -> the key is coloured again", Changed(pageBase, Snapshot()) == "");
    await Send("POST", "/ui-state", PageState(k => k == "pomodoro" || k == "speed"));
    Check("page: several switches at once -> exactly those keys change", Changed(pageBase, Snapshot()) == "volume,blink,pomodoro,distance,behind", $"changed: {Changed(pageBase, Snapshot())}");
    UiSettings.Changed -= onRedraw;
    Check("and the page is told about key presses (revision grows)", JsonDocument.Parse(UiSettings.ToJson()).RootElement.GetProperty("rev").GetInt64() > 1);
}
catch (Exception ex)
{
    Console.WriteLine($"  skip  key image checks: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("Persistence");
var before = UiSettings.ToJson();
UiSettings.Init();
Check("state survives a reload", JsonDocument.Parse(UiSettings.ToJson()).RootElement.GetProperty("features").GetProperty("volume").GetProperty("enabled").GetBoolean() == false);

Console.WriteLine("Keypad drawing (needs the real PluginApi.dll; skipped if it cannot load)");
try
{
    PomodoroState.Reset();
    var scene = KeypadScreen.Build();
    var img = scene.Tile(1, 1, 116);
    Check("Pomodoro timer scene draws a tile", img != null);
    TriggerBus.Publish(new TriggerMessage("break_due", 1.0, true));
    NotificationPolicy.Evaluate(new TriggerMessage("break_due", 1.0, true));
    SessionState.OnTrigger(new TriggerMessage("break_due", 1.0), new TriggerRule { KeypadColor = "#123456" });
    Check("generic reminder scene draws", SessionState.GenericActive && KeypadScreen.Build().Tile(0, 0, 116) != null);
    SessionState.DismissAlert();
    Check("widget keys draw nothing special without an alert", KeypadScreen.AlertKey(116) == null);
    foreach (var t in new[] { "person_behind", "break_due", "speak_up", "blink_low", "pace_too_fast", "time_warning" })
    {
        SessionState.OnTrigger(new TriggerMessage(t, 1.0));
        Check($"widget keys draw the alert for {t}", KeypadScreen.AlertKey(116) != null && KeypadScreen.AlertKey(80) != null);
        SessionState.DismissAlert();
    }

    Check("a dismissed alert leaves the widget keys alone", !SessionState.TakeoverActive && KeypadScreen.AlertKey(116) == null);
}
catch (Exception ex)
{
    Console.WriteLine($"  skip  keypad drawing: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine("Detector features through the settings (duck, snail, owl, control signals)");
UiSettings.ApplyProfile("everyday");
// NOTE: runs last on purpose: the future timestamps below move the policy's per-trigger clocks, which would make later real-time checks drop.
var later = DateTime.UtcNow.AddHours(1);   // past every cooldown, so the page's channel settings are what we test
var seenRaw = new List<String>();
TriggerBus.Received += m => seenRaw.Add(m.Trigger);
var duck = NotificationPolicy.Decide(new TriggerMessage("speak_up", 0.8), later);
Check("speak_up reaches the screen channel by default", duck != null && duck.Overlay, duck == null ? "dropped" : "no overlay");
SessionState.OnOverlay(duck.Message);
Check("duck shows on screen", SessionState.OverlaySnapshot().animal == "duck");
SessionState.ClearSpeakAlert();
Check("speak_ok ends the duck", SessionState.OverlaySnapshot().animal == null);
var snail = NotificationPolicy.Decide(new TriggerMessage("pace_too_fast", 0.9), later.AddHours(1));
Check("pace_too_fast reaches the screen channel", snail != null && snail.Overlay);
SessionState.OnOverlay(snail.Message);
Check("snail shows on screen", SessionState.OverlaySnapshot().animal == "snail");
SessionState.ClearPaceAlert();
Check("pace_ok ends the snail", SessionState.OverlaySnapshot().animal == null);
var owl = NotificationPolicy.Decide(new TriggerMessage("blink_low", 0.8), later.AddHours(2));
Check("blink_low reaches the screen channel", owl != null && owl.Overlay);
SessionState.OnOverlay(owl.Message);
Check("owl shows on screen", SessionState.OverlaySnapshot().animal == "owl");
SessionState.ClearBlinkAlert();
Check("blink_detected ends the owl", SessionState.OverlaySnapshot().animal == null);
seenRaw.Clear();
Check("/signal/speak_up/false releases the duck (speak_ok)", (await Send("GET", "/signal/speak_up/false")).code == 200 && seenRaw.Contains("speak_ok"));
Check("speak_ok is a known trigger", (await Send("GET", "/triggers")).body.Contains("speak_ok"));
Check("/signal/presence/false -> away", (await Send("GET", "/signal/presence/false")).code == 200 && !PomodoroState.AtDesk);
Check("/signal/presence/true -> at desk", (await Send("GET", "/signal/presence/true")).code == 200 && PomodoroState.AtDesk);
UiSettings.SetFeatureEnabled("pomodoro", false);
Check("break_due is DROPPED while Pomodoro is off on the page", NotificationPolicy.Decide(new TriggerMessage("break_due", 0.7), later.AddHours(3)) == null);
UiSettings.SetFeatureEnabled("pomodoro", true);
Check("break_due is delivered once Pomodoro is on, mouse buzzes on the FIRST message (the plugin timer sends it once)", NotificationPolicy.Decide(new TriggerMessage("break_due", 0.7), later.AddHours(4)) is { } bdue && bdue.Haptic && bdue.Visual && bdue.Overlay);
UiSettings.SetFeatureEnabled("pomodoro", false);

server.Stop();
Console.WriteLine();
Console.WriteLine($"{pass} passed, {fail} failed");
try { Directory.Delete(tmp, true); } catch { }
return fail == 0 ? 0 : 1;

async Task<Boolean> EnablePomodoro(Boolean on)
{
    var r = await Send("POST", "/ui-state", state.Replace("\"pomodoro\":{\"enabled\":false", $"\"pomodoro\":{{\"enabled\":{(on ? "true" : "false")}"));
    return r.code == 200;
}

// A sound device that records what the engine does.
class FakeAudio : ISystemAudio
{
    public String Name => "fake";
    public Int32? Volume { get; set; }
    public Boolean? Muted { get; set; } = false;
    public Int32 Sets { get; private set; }
    public Int32 MaxJump { get; set; }
    public Boolean Log { get; set; }
    public Int32? GetVolume() => Volume;
    public Boolean? GetMuted() => Muted;
    public Boolean SetVolume(Int32 percent)
    {
        if (Volume.HasValue) { MaxJump = Math.Max(MaxJump, Math.Abs(percent - Volume.Value)); }
        Volume = percent;
        Sets++;
        if (Log) { Console.WriteLine($"fake audio: volume -> {percent}"); }
        return true;
    }
    public Boolean SetMuted(Boolean muted) { Muted = muted; if (Log) { Console.WriteLine($"fake audio: muted -> {muted}"); } return true; }
}
