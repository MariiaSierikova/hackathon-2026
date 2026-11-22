using Loupedeck;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Loupedeck.WorkHealthCoachPlugin;

// Test harness: exercises the plugin code that does not need Options+ or a device.
// Safety: settings files are redirected to a temp folder; the run aborts if that did not work.

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
    var srv = new LocalServer();
    srv.Start();
    var ticker = new System.Timers.Timer(1000) { AutoReset = true };
    ticker.Elapsed += (_, _) => { SessionState.Tick(); PomodoroState.Tick(); };
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

Console.WriteLine("Persistence");
var before = UiSettings.ToJson();
UiSettings.Init();
Check("state survives a reload", JsonDocument.Parse(UiSettings.ToJson()).RootElement.GetProperty("features").GetProperty("speed").GetProperty("cooldown").GetInt32() == 30);

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
}
catch (Exception ex)
{
    Console.WriteLine($"  skip  keypad drawing: {ex.GetType().Name}: {ex.Message}");
}

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
