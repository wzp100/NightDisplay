using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NightDisplay
{
    public sealed class WebAsset { public byte[] Bytes; public string ContentType; }
    public static class WebAssets
    {
        static readonly Dictionary<string, WebAsset> assets = Load();
        static Dictionary<string, WebAsset> Load()
        {
            var result = new Dictionary<string, WebAsset>(StringComparer.Ordinal);
            Add(result, "/", "index.html", "text/html; charset=utf-8");
            Add(result, "/app.css", "app.css", "text/css; charset=utf-8");
            Add(result, "/app.js", "app.js", "application/javascript; charset=utf-8");
            Add(result, "/icon.png", "icon.png", "image/png");
            return result;
        }
        static void Add(Dictionary<string, WebAsset> result, string route, string name, string type)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NightDisplay.Web." + name))
            using (var data = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("缺少网页资源：" + name);
                stream.CopyTo(data);
                result.Add(route, new WebAsset { Bytes = data.ToArray(), ContentType = type });
            }
        }
        public static bool TryGet(string route, out WebAsset asset) { return assets.TryGetValue(route, out asset); }
    }
}
