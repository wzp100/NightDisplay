using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NightDisplay
{
    public sealed class LanServer : IDisposable
    {
        readonly int port;
        readonly string token;
        readonly Func<object> status;
        readonly Func<string, Task<object>> command;
        readonly SemaphoreSlim slots = new SemaphoreSlim(8);
        TcpListener listener;
        volatile bool stopped;
        long lastAction;
        readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        public string PhoneBaseUrl { get { return "http://" + PhoneAddress() + ":" + port + "/"; } }
        public string PhoneUrl { get { return PhoneBaseUrl + "?token=" + token; } }
        public string LocalUrl { get { return "http://127.0.0.1:" + port + "/?token=" + token; } }
        public LanServer(int port, string token, Func<object> status, Func<string, Task<object>> command)
        { this.port = port; this.token = token; this.status = status; this.command = command; }
        public void Start()
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(16);
            Task.Run(new Func<Task>(AcceptLoop));
            AppState.Log("局域网控制已启动，端口 " + port);
        }
        public static bool LocalAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            byte[] a = address.GetAddressBytes();
            return a.Length == 4 && (a[0] == 10 || a[0] == 192 && a[1] == 168 || a[0] == 172 && a[1] >= 16 && a[1] <= 31 || a[0] == 169 && a[1] == 254);
        }
        static string PhoneAddress()
        {
            var choices = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || n.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && LocalAddress(a.Address) && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString()).ToArray();
            return choices.FirstOrDefault() ?? "127.0.0.1";
        }
        async Task AcceptLoop()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch { break; }
                if (!slots.Wait(0)) { client.Close(); continue; }
                lock (clients) { if (stopped) { client.Close(); slots.Release(); break; } clients.Add(client); }
                StartClient(client);
            }
        }
        void StartClient(TcpClient client)
        {
            Task.Run(async delegate { try { await Serve(client); } catch (Exception ex) { if (!stopped) AppState.Log("网页请求失败：" + ex.GetType().Name); } finally { client.Close(); lock (clients) clients.Remove(client); slots.Release(); } });
        }
        bool Authorized(string candidate)
        {
            if (candidate == null || candidate.Length != token.Length) return false;
            int difference = 0; for (int i = 0; i < token.Length; i++) difference |= token[i] ^ candidate[i];
            return difference == 0;
        }
        async Task Serve(TcpClient client)
        {
            var remote = (IPEndPoint)client.Client.RemoteEndPoint;
            using (var deadline = new System.Threading.Timer(delegate { client.Close(); }, null, 4000, Timeout.Infinite))
            using (var stream = client.GetStream())
            {
                if (!LocalAddress(remote.Address)) { await Reply(stream, 403, "text/plain", "仅允许局域网访问。"); return; }
                var reader = new RequestReader(stream);
                string first = await reader.ReadLine(4096);
                string[] parts = first.Split(' ');
                if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.")) { await Reply(stream, 400, "text/plain", "请求无效。"); return; }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int headerLength = 0;
                while (true)
                {
                    string line = await reader.ReadLine(4096); if (line.Length == 0) break;
                    headerLength += line.Length;
                    if (headerLength > 16384) { await Reply(stream, 413, "text/plain", "请求过长。"); return; }
                    int split = line.IndexOf(':'); if (split < 1) { await Reply(stream, 400, "text/plain", "请求无效。"); return; }
                    string key = line.Substring(0, split).Trim();
                    if (headers.ContainsKey(key)) { await Reply(stream, 400, "text/plain", "重复请求头。"); return; }
                    headers[key] = line.Substring(split + 1).Trim();
                }
                string host;
                if (!headers.TryGetValue("Host", out host) || !ValidHost(host)) { await Reply(stream, 403, "text/plain", "访问地址无效。"); return; }
                string route = parts[1].Split('?')[0];
                WebAsset asset;
                if (parts[0] == "GET" && WebAssets.TryGet(route, out asset))
                {
                    deadline.Change(Timeout.Infinite, Timeout.Infinite);
                    await Reply(stream, 200, asset.ContentType, asset.Bytes); return;
                }
                if (parts[0] == "GET" && route == "/favicon.ico") { await Reply(stream, 204, "text/plain", ""); return; }
                string supplied;
                if (!headers.TryGetValue("X-NightDisplay-Token", out supplied) || !Authorized(supplied)) { await Reply(stream, 401, "application/json", "{\"error\":\"访问密钥无效，请从电脑重新复制完整链接。\"}"); return; }
                string origin;
                if (headers.TryGetValue("Origin", out origin) && origin != "http://" + host) { await Reply(stream, 403, "application/json", "{\"error\":\"来源无效。\"}"); return; }
                if (parts[0] == "GET" && route == "/api/status") { deadline.Change(Timeout.Infinite, Timeout.Infinite); await Json(stream, status()); return; }
                if (parts[0] != "POST" || !route.StartsWith("/api/")) { await Reply(stream, 404, "text/plain", "页面不存在。"); return; }
                string type;
                if (!headers.TryGetValue("Content-Type", out type) || !type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) || headers.ContainsKey("Transfer-Encoding")) { await Reply(stream, 400, "application/json", "{\"error\":\"请求格式无效。\"}"); return; }
                string lengthText; int length;
                if (!headers.TryGetValue("Content-Length", out lengthText) || !int.TryParse(lengthText, out length) || length < 0 || length > 2048) { await Reply(stream, 400, "application/json", "{\"error\":\"请求长度无效。\"}"); return; }
                await reader.ReadBody(length);
                deadline.Change(Timeout.Infinite, Timeout.Infinite);
                string action = route.Substring(5);
                if (action != "physical" && action != "virtual" && action != "toggle" && action != "test") { await Reply(stream, 404, "text/plain", "操作不存在。"); return; }
                long now = DateTime.UtcNow.Ticks;
                long previous = Interlocked.Read(ref lastAction);
                if (now - previous < TimeSpan.FromMilliseconds(500).Ticks || Interlocked.CompareExchange(ref lastAction, now, previous) != previous) { await Reply(stream, 429, "application/json", "{\"error\":\"操作太快，请稍后重试。\"}"); return; }
                string actionError = null;
                object actionResult = null;
                try
                {
                    var task = command(action);
                    if (await Task.WhenAny(task, Task.Delay(8000)) != task) { await Reply(stream, 503, "application/json", "{\"error\":\"电脑暂未响应，恢复守护进程会在无响应时恢复实体屏。\"}"); return; }
                    actionResult = await task;
                }
                catch (Exception ex) { actionError = ex.Message; }
                if (actionError != null) await Reply(stream, 409, "application/json", new JavaScriptSerializer().Serialize(new { error = actionError }));
                else await Json(stream, actionResult);
            }
        }
        bool ValidHost(string host)
        {
            Uri uri; if (!Uri.TryCreate("http://" + host, UriKind.Absolute, out uri) || uri.Port != port) return false;
            if (uri.Host == "localhost") return true;
            IPAddress address; if (!IPAddress.TryParse(uri.Host, out address) || !LocalAddress(address)) return false;
            if (IPAddress.IsLoopback(address)) return true;
            return NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address));
        }
        static Task Json(NetworkStream stream, object value) { return Reply(stream, 200, "application/json; charset=utf-8", new JavaScriptSerializer().Serialize(value)); }
        static async Task Reply(NetworkStream stream, int code, string contentType, string body)
        {
            await Reply(stream, code, contentType, Encoding.UTF8.GetBytes(body));
        }
        static async Task Reply(NetworkStream stream, int code, string contentType, byte[] payload)
        {
            stream.WriteTimeout = 4000;
            string header = "HTTP/1.1 " + code + " " + (code == 200 ? "OK" : "Response") + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(bytes, 0, bytes.Length); await stream.WriteAsync(payload, 0, payload.Length);
        }
        public void Dispose() { stopped = true; if (listener != null) listener.Stop(); lock (clients) foreach (var client in clients) client.Close(); }
    }
}
