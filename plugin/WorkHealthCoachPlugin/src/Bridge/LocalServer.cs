namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    // Tiny localhost-only HTTP API. The camera/mic code (or scripts/trigger.py) sends triggers here.
    //   POST /trigger   {"trigger":"pace_too_fast","severity":0.8}
    //   GET  /health
    //   GET  /triggers
    public class LocalServer
    {
        public const Int32 Port = 8787;

        private HttpListener _listener;
        private CancellationTokenSource _cts;

        public void Start()
        {
            try
            {
                this._cts = new CancellationTokenSource();
                this._listener = new HttpListener();
                this._listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                this._listener.Prefixes.Add($"http://localhost:{Port}/");
                this._listener.Start();
                _ = Task.Run(() => this.LoopAsync(this._cts.Token));
                PluginLog.Info($"LocalServer listening on http://127.0.0.1:{Port}/");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"LocalServer could not start on port {Port}");
            }
        }

        public void Stop()
        {
            try
            {
                this._cts?.Cancel();
                this._listener?.Stop();
                this._listener?.Close();
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "LocalServer stop");
            }
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && this._listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await this._listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => this.HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
                var method = ctx.Request.HttpMethod;

                if (method == "GET" && path == "/health")
                {
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else if (method == "GET" && path == "/overlay")
                {
                    var (animal, age) = SessionState.OverlaySnapshot();
                    await Reply(ctx, 200, $"{{\"animal\":{(animal == null ? "null" : $"\"{animal}\"")},\"ageMs\":{age}}}");
                }
                else if (method == "GET" && path == "/triggers")
                {
                    var names = String.Join(",", Triggers.All.Select(n => $"\"{n}\""));
                    await Reply(ctx, 200, $"{{\"triggers\":[{names}]}}");
                }
                else if (method == "GET" && path == "/settings")
                {
                    await Reply(ctx, 200, NotificationPolicy.Settings.ToJson());
                }
                else if (method == "POST" && path == "/settings")
                {
                    String body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    var s = PolicySettings.FromJson(body);
                    s.Preset = "custom";
                    NotificationPolicy.SetSettings(s);
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else if (method == "POST" && path == "/preset")
                {
                    using var doc = JsonDocument.Parse(await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync());
                    NotificationPolicy.SetPreset(doc.RootElement.GetProperty("name").GetString());
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else if (method == "POST" && path == "/snooze")
                {
                    using var doc = JsonDocument.Parse(await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync());
                    NotificationPolicy.Snooze(doc.RootElement.TryGetProperty("minutes", out var mm) && mm.TryGetInt32(out var mv) ? mv : 15);
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else if (method == "POST" && path == "/metrics")
                {
                    String body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    using var doc = JsonDocument.Parse(body);
                    Double? wpm = doc.RootElement.TryGetProperty("wpm", out var w) && w.TryGetDouble(out var wv) ? wv : null;
                    SessionState.SetMetrics(wpm);
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else if (method == "POST" && path == "/trigger")
                {
                    String body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync();
                    }

                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var trigger = root.TryGetProperty("trigger", out var t) ? t.GetString() : null;
                    var severity = root.TryGetProperty("severity", out var s) && s.TryGetDouble(out var d) ? d : 1.0;

                    if (String.IsNullOrEmpty(trigger) || !Triggers.IsKnown(trigger))
                    {
                        await Reply(ctx, 400, "{\"error\":\"unknown or missing trigger\"}");
                        return;
                    }

                    var force = root.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;
                    PluginLog.Info($"Trigger received: {trigger} severity={severity}{(force ? " FORCE" : String.Empty)}");
                    TriggerBus.Publish(new TriggerMessage(trigger, severity, force));
                    await Reply(ctx, 200, "{\"ok\":true}");
                }
                else
                {
                    await Reply(ctx, 404, "{\"error\":\"not found\"}");
                }
            }
            catch (JsonException)
            {
                await Reply(ctx, 400, "{\"error\":\"invalid json\"}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "LocalServer request failed");
                try { await Reply(ctx, 500, "{\"error\":\"server error\"}"); } catch { }
            }
        }

        private static async Task Reply(HttpListenerContext ctx, Int32 status, String json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
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
    }
}
