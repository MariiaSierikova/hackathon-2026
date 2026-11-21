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
    //
    // Plain TcpListener on 127.0.0.1 (not HttpListener): on Windows HttpListener needs admin rights / URL reservations
    // for some prefixes and fails silently for normal users; a loopback TcpListener works everywhere.
    public class LocalServer
    {
        public const Int32 Port = 8787;

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
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
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

                    while (body.Length < length && body.Length < 1_000_000)
                    {
                        var n = await stream.ReadAsync(buf, timeout.Token);
                        if (n == 0)
                        {
                            break;
                        }

                        body.Write(buf, 0, n);
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

                    var payload = Encoding.UTF8.GetBytes(result.json);
                    var reason = result.status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", _ => "Error" };
                    var header = $"HTTP/1.1 {result.status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token);
                    await stream.WriteAsync(payload, timeout.Token);
                    await stream.FlushAsync(timeout.Token);
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
                PluginLog.Info($"Trigger received: {trigger} severity={severity}{(force ? " FORCE" : String.Empty)}");
                TriggerBus.Publish(new TriggerMessage(trigger, severity, force));
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
        public static readonly String[] Controls = { "blink_detected", "pace_ok" };

        public static IEnumerable<String> All => HapticPlayer.PatternNames.Concat(Controls);

        public static Boolean IsKnown(String name) => All.Contains(name);

        // Real alerts (everything except the two control triggers).
        public static IEnumerable<String> Alerts => HapticPlayer.PatternNames;

        // What "false" means for an alert: the control trigger that ends it (or null: nothing to end).
        public static String ReleaseFor(String alert) => alert switch
        {
            "pace_too_fast" => "pace_ok",
            "blink_low" => "blink_detected",
            _ => null,
        };
    }
}
