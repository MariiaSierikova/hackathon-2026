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
                else if (method == "GET" && path == "/triggers")
                {
                    var names = String.Join(",", HapticPlayer.PatternNames.Select(n => $"\"{n}\""));
                    await Reply(ctx, 200, $"{{\"triggers\":[{names}]}}");
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

                    if (String.IsNullOrEmpty(trigger) || !HapticPlayer.PatternNames.Contains(trigger))
                    {
                        await Reply(ctx, 400, "{\"error\":\"unknown or missing trigger\"}");
                        return;
                    }

                    PluginLog.Info($"Trigger received: {trigger} severity={severity}");
                    TriggerBus.Publish(new TriggerMessage(trigger, severity));
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
