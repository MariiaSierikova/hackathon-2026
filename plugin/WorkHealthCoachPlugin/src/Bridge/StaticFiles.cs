namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    // Serves the settings page and its icons/sounds. They are embedded in the plugin DLL (csproj: Web/**), because the
    // plugin host gives no reliable folder to read files from.
    //   GET /  ->  web/index.html          GET /assets/icons/speed.png  ->  web/assets/icons/speed.png
    public static class StaticFiles
    {
        private static readonly Lazy<Dictionary<String, String>> Index = new(Build);

        private static Dictionary<String, String> Build()
        {
            var d = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in typeof(StaticFiles).Assembly.GetManifestResourceNames())
            {
                var key = name.Replace('\\', '/');
                if (key.StartsWith("web/", StringComparison.OrdinalIgnoreCase))
                {
                    d["/" + key.Substring(4)] = name;
                }
            }

            return d;
        }

        public static Int32 Count => Index.Value.Count;

        public static Boolean TryGet(String path, out Byte[] data, out String contentType)
        {
            data = null;
            contentType = null;
            var p = String.IsNullOrEmpty(path) || path == "/" ? "/index.html" : path;
            if (p.Contains("..") || !Index.Value.TryGetValue(p, out var name))
            {
                return false;
            }

            using var s = typeof(StaticFiles).Assembly.GetManifestResourceStream(name);
            if (s == null)
            {
                return false;
            }

            using var ms = new MemoryStream();
            s.CopyTo(ms);
            data = ms.ToArray();
            contentType = TypeOf(p);
            return true;
        }

        private static String TypeOf(String path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream",
        };
    }
}
