namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    // Tiny localhost-only HTTP API. The camera/mic code (or scripts/trigger.py) sends triggers here.
    //   POST /trigger   {"trigger":"pace_too_fast","severity":0.8}
    //   GET  /health    GET /triggers    GET /overlay    GET/POST /settings    POST /preset /snooze /metrics
    //   GET  /  (the settings page, embedded)    GET/POST /ui-state    GET /pomodoro    POST /pomodoro/<action>    POST /presence
    //
    // Plain TcpListener on 127.0.0.1 (not HttpListener): on Windows HttpListener needs admin rights / URL reservations
    // for some prefixes and fails silently for normal users; a loopback TcpListener works everywhere.
    public class LocalServer
    {
        // 8787 for real use; WORKHEALTH_PORT lets tests run a second server without touching the plugin inside Options+.
        public static readonly Int32 Port = Int32.TryParse(Environment.GetEnvironmentVariable("WORKHEALTH_PORT"), out var p) && p > 0 ? p : 8787;

        // When this plugin was built (stamped by MSBuild). Shown by /health and in the log, so a stale build is easy to
        // spot: on Windows a still-loaded DLL can make `dotnet build` leave the old plugin running.
        public static readonly String BuildStamp = ReadBuildStamp();

        private static String ReadBuildStamp()
        {
            try
            {
                var attr = typeof(LocalServer).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                return attr.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public void Start()
        {
            try
            {
                this._cts = new CancellationTokenSource();
                this._listener = new TcpListener(IPAddress.Loopback, Port);
                this._listener.Start();
                _ = Task.Run(() => this.AcceptLoopAsync(this._cts.Token));
                PluginLog.Info($"LocalServer listening on http://127.0.0.1:{Port}/ (plugin build {BuildStamp})");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"LocalServer could not start on port {Port} (is another program using it?)");
            }
        }

        public void Stop()
        {
            try
            {
                this._cts?.Cancel();
                this._listener?.Stop();
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "LocalServer stop");
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await this._listener.AcceptTcpClientAsync(token);
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => this.ServeAsync(client, token));
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var stream = client.GetStream();

                    // read until the end of the headers
                    var ms = new MemoryStream();
                    var buf = new Byte[4096];
                    var headerEnd = -1;
                    while (headerEnd < 0 && ms.Length < 65536)
                    {
                        var n = await stream.ReadAsync(buf, timeout.Token);
                        if (n == 0)
                        {
                            return;
                        }

                        ms.Write(buf, 0, n);
                        headerEnd = IndexOf(ms.GetBuffer(), (Int32)ms.Length, new Byte[] { 13, 10, 13, 10 });
                    }

                    if (headerEnd < 0)
                    {
                        return;
                    }

                    var head = Encoding.ASCII.GetString(ms.GetBuffer(), 0, headerEnd).Split("\r\n");
                    var request = head[0].Split(' ');
                    var method = request[0];
                    var target = request.Length > 1 ? request[1] : String.Empty;
                    var path = target.Split('?')[0].TrimEnd('/');
                    var query = target.Contains('?') ? target.Substring(target.IndexOf('?') + 1) : String.Empty;
                    var length = 0;
                    foreach (var line in head.Skip(1))
                    {
                        if (line.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase))
                        {
                            Int32.TryParse(line.Substring(15).Trim(), out length);
                        }
                    }

                    // read the body (some bytes may already be here)
                    var body = new MemoryStream();
                    var already = (Int32)ms.Length - (headerEnd + 4);
                    if (already > 0)
                    {
                        body.Write(ms.GetBuffer(), headerEnd + 4, Math.Min(already, length));
                    }

                    while (body.Length < length && body.Length < 8_000_000)
                    {
                        var n = await stream.ReadAsync(buf, timeout.Token);
                        if (n == 0)
                        {
                            break;
                        }

                        body.Write(buf, 0, n);
                    }

                    if (method == "GET" && StaticFiles.TryGet(path, out var fileBytes, out var fileType))
                    {
                        await Respond(stream, 200, fileType, fileBytes, timeout.Token);
                        return;
                    }

                    (Int32 status, String json) result;
                    try
                    {
                        result = Route(method, path, query, Encoding.UTF8.GetString(body.ToArray()));
                    }
                    catch (JsonException)
                    {
                        result = (400, "{\"error\":\"invalid json\"}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, "LocalServer request failed");
                        result = (500, "{\"error\":\"server error\"}");
                    }

                    await Respond(stream, result.status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(result.json), timeout.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
                {
                    // client went away or was too slow: nothing to do
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "LocalServer connection failed");
                }
            }
        }

        private static async Task Respond(NetworkStream stream, Int32 status, String contentType, Byte[] payload, CancellationToken token)
        {
            var reason = status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", _ => "Error" };
            var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nCache-Control: no-cache\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
            await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        private static Int32 IndexOf(Byte[] data, Int32 length, Byte[] pattern)
        {
            for (var i = 0; i <= length - pattern.Length; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Length && match; j++)
                {
                    match = data[i + j] == pattern[j];
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static (Int32 status, String json) Route(String method, String path, String query, String body)
        {
            const String Ok = "{\"ok\":true}";

            if (method == "GET" && path == "/health")
            {
                return (200, $"{{\"ok\":true,\"build\":\"{BuildStamp}\"}}");
            }

            if (method == "GET" && path == "/overlay")
            {
                var (animal, age) = SessionState.OverlaySnapshot();
                return (200, $"{{\"animal\":{(animal == null ? "null" : $"\"{animal}\"")},\"ageMs\":{age}}}");
            }

            if (method == "GET" && path == "/triggers")
            {
                var names = String.Join(",", Triggers.All.Select(n => $"\"{n}\""));
                return (200, $"{{\"triggers\":[{names}]}}");
            }

            if (method == "GET" && path == "/settings")
            {
                return (200, NotificationPolicy.Settings.ToJson());
            }

            if (method == "POST" && path == "/settings")
            {
                var s = PolicySettings.FromJson(body);
                s.Preset = "custom";
                NotificationPolicy.SetSettings(s);
                return (200, Ok);
            }

            if (method == "POST" && path == "/preset")
            {
                using var doc = JsonDocument.Parse(body);
                NotificationPolicy.SetPreset(doc.RootElement.GetProperty("name").GetString());
                return (200, Ok);
            }

            if (method == "POST" && path == "/snooze")
            {
                using var doc = JsonDocument.Parse(body);
                NotificationPolicy.Snooze(doc.RootElement.TryGetProperty("minutes", out var mm) && mm.TryGetInt32(out var mv) ? mv : 15);
                return (200, Ok);
            }

            if (method == "POST" && path == "/metrics")
            {
                using var doc = JsonDocument.Parse(body);
                Double? wpm = doc.RootElement.TryGetProperty("wpm", out var w) && w.TryGetDouble(out var wv) ? wv : null;
                SessionState.SetMetrics(wpm);
                return (200, Ok);
            }

            // ---- settings page ----
            if (path == "/ui-state")
            {
                if (method == "GET")
                {
                    return (200, UiSettings.ToJson());
                }

                if (method == "POST")
                {
                    return UiSettings.Set(body, out var err)
                        ? (200, $"{{\"ok\":true,\"rev\":{UiSettings.Rev}}}")
                        : (400, $"{{\"error\":{JsonSerializer.Serialize(err)}}}");
                }
            }

            if (method == "GET" && path == "/pomodoro")
            {
                return (200, PomodoroState.ToJson());
            }

            if (method == "POST" && path.StartsWith("/pomodoro/", StringComparison.Ordinal))
            {
                switch (path.Substring("/pomodoro/".Length))
                {
                    case "start": UiSettings.SetFeatureEnabled("pomodoro", true); break;   // feature on == timer on the Keypad
                    case "stop": UiSettings.SetFeatureEnabled("pomodoro", false); break;
                    case "toggle": PomodoroState.TogglePause(); break;
                    case "skip": PomodoroState.Skip(); break;
                    case "reset": PomodoroState.Reset(); break;
                    case "demo-away": PomodoroState.DemoSkipAway(); break;
                    default: return (404, "{\"error\":\"unknown pomodoro action\"}");
                }

                return (200, PomodoroState.ToJson());
            }

            if (method == "POST" && path == "/presence")
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("atDesk", out var at) || at.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return (400, "{\"error\":\"send {\\\"atDesk\\\":true|false}\"}");
                }

                Presence.Set(at.GetBoolean());
                return (200, Ok);
            }

            // ---- volume when far: distance without any camera code ----
            //   POST /distance {"ratio":0.6}   face size relative to normal (1.0 = normal distance, 0.6 = 40 % smaller)
            //   POST /distance {"face":0.11}   raw face width, needs /distance/calibrate first
            //   POST /distance/calibrate       "this is my normal distance" (optional {"face":0.18}; default = the latest face)
            //   GET  /distance                 state + the real system volume
            if (path == "/distance" && method == "GET")
            {
                return (200, FarVolume.ToJson());
            }

            if (method == "POST" && path == "/distance")
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                if (r.TryGetProperty("ratio", out var rv) && rv.TryGetDouble(out var ratio))
                {
                    FarVolume.SetRatio(ratio);
                    return (200, Ok);
                }

                if (r.TryGetProperty("face", out var fv) && fv.TryGetDouble(out var face))
                {
                    return FarVolume.SetFace(face) ? (200, Ok) : (400, "{\"error\":\"no baseline yet: POST /distance/calibrate first\"}");
                }

                return (400, "{\"error\":\"send {\\\"ratio\\\":0.6} or {\\\"face\\\":0.11}\"}");
            }

            if (method == "POST" && path == "/distance/calibrate")
            {
                Double? face = null;
                if (!String.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("face", out var fv) && fv.TryGetDouble(out var f))
                    {
                        face = f;
                    }
                }

                return FarVolume.Calibrate(face) ? (200, Ok) : (400, "{\"error\":\"no face width yet: send a sample first or pass {\\\"face\\\":0.18}\"}");
            }

            // ---- person behind: samples of a second face (no camera code here) ----
            //   POST /behind {"id":2,"x":0.62,"size":0.07}   or   {"samples":[{...},{...}]}     POST /behind/clear     GET /behind
            //   POST /behind/do/<hide|back|lock|music|talk|ok|snooze>      what the Actions Ring buttons do
            if (path == "/behind" && method == "GET")
            {
                return (200, PersonBehind.ToJson());
            }

            if (method == "POST" && path == "/behind")
            {
                using var doc = JsonDocument.Parse(body);
                var added = 0;
                void One(JsonElement e)
                {
                    if (e.TryGetProperty("x", out var xv) && xv.TryGetDouble(out var x) && e.TryGetProperty("size", out var sv) && sv.TryGetDouble(out var size))
                    {
                        var id = e.TryGetProperty("id", out var iv) && iv.TryGetInt32(out var i) ? i : 0;
                        PersonBehind.AddSample(id, x, size);
                        added++;
                    }
                }

                if (doc.RootElement.TryGetProperty("samples", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray())
                    {
                        One(e);
                    }
                }
                else
                {
                    One(doc.RootElement);
                }

                return added > 0 ? (200, $"{{\"ok\":true,\"samples\":{added}}}") : (400, "{\"error\":\"send id, x and size\"}");
            }

            if (method == "POST" && path == "/behind/clear")
            {
                PersonBehind.Clear();
                return (200, Ok);
            }

            if (method == "POST" && path.StartsWith("/behind/do/", StringComparison.Ordinal))
            {
                var action = path.Substring("/behind/do/".Length);
                PluginLog.Info($"Person-behind action from the API: {action}");
                return BehindResponse.Run(action) ? (200, PersonBehind.ToJson()) : (404, "{\"error\":\"unknown action\"}");
            }

            if (method == "POST" && path == "/trigger")
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var trigger = root.TryGetProperty("trigger", out var t) ? t.GetString() : null;
                var severity = root.TryGetProperty("severity", out var s) && s.TryGetDouble(out var d) ? d : 1.0;

                if (String.IsNullOrEmpty(trigger) || !Triggers.IsKnown(trigger))
                {
                    return (400, "{\"error\":\"unknown or missing trigger\"}");
                }

                var force = root.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;
                var only = root.TryGetProperty("only", out var on0) && on0.ValueKind == JsonValueKind.String ? on0.GetString() : null;
                PluginLog.Info($"Trigger received: {trigger} severity={severity}{(force ? " FORCE" : String.Empty)}{(only != null ? " only=" + only : String.Empty)}");
                TriggerBus.Publish(new TriggerMessage(trigger, severity, force, only));
                return (200, Ok);
            }

            // Simplest interface: a plain True/False.
            //   GET or POST /pace/true            == "speech is too fast"     (/pace/false = "pace is fine again")
            //   GET or POST /signal/<trigger>/true | false      e.g. /signal/blink_low/true
            // optional query: ?severity=0.8   ?force=1 (development: skip the notification policy)
            if (method == "GET" || method == "POST")
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                String name = null;
                String value = null;
                if (parts.Length == 2 && parts[0] == "pace")
                {
                    (name, value) = ("pace_too_fast", parts[1]);
                }
                else if (parts.Length == 3 && parts[0] == "signal")
                {
                    (name, value) = (parts[1], parts[2]);
                }

                if (name != null)
                {
                    var on = ParseBool(value);
                    if (on == null)
                    {
                        return (400, "{\"error\":\"use true or false\"}");
                    }

                    // camera presence for the Pomodoro timer: /signal/presence/true (someone at the desk) | false
                    if (name == "presence")
                    {
                        Presence.Set(on == true);
                        return (200, Ok);
                    }

                    var q = ParseQuery(query);
                    var severity = q.TryGetValue("severity", out var sv) && Double.TryParse(sv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sd) ? sd : 1.0;
                    var force = q.TryGetValue("force", out var fv) && ParseBool(fv) == true;

                    if (on == true)
                    {
                        if (!Triggers.Alerts.Contains(name))
                        {
                            return (400, "{\"error\":\"unknown trigger\"}");
                        }

                        PluginLog.Info($"Signal TRUE: {name} severity={severity}{(force ? " FORCE" : String.Empty)}");
                        TriggerBus.Publish(new TriggerMessage(name, severity, force));
                    }
                    else
                    {
                        var release = Triggers.ReleaseFor(name);
                        PluginLog.Info($"Signal FALSE: {name}{(release != null ? $" -> {release}" : " (nothing to end)")}");
                        if (release != null)
                        {
                            TriggerBus.Publish(new TriggerMessage(release, 1.0));
                        }
                    }

                    return (200, Ok);
                }
            }

            return (404, "{\"error\":\"not found\"}");
        }

        private static Boolean? ParseBool(String v) => (v ?? String.Empty).Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => null,
        };

        private static System.Collections.Generic.Dictionary<String, String> ParseQuery(String query)
        {
            var d = new System.Collections.Generic.Dictionary<String, String>();
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                d[kv[0]] = kv.Length > 1 ? kv[1] : "true";
            }

            return d;
        }
    }
}

namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Every trigger name the plugin accepts.
    public static class Triggers
    {
        public static readonly String[] Controls = { "blink_detected", "pace_ok", "speak_ok" };

        public static IEnumerable<String> All => HapticPlayer.PatternNames.Concat(Controls);

        public static Boolean IsKnown(String name) => All.Contains(name);

        // Real alerts (everything except the two control triggers).
        public static IEnumerable<String> Alerts => HapticPlayer.PatternNames;

        // What "false" means for an alert: the control trigger that ends it (or null: nothing to end).
        public static String ReleaseFor(String alert) => alert switch
        {
            "pace_too_fast" => "pace_ok",
            "blink_low" => "blink_detected",
            "speak_up" => "speak_ok",
            _ => null,
        };
    }
}
