using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NightDisplay;

class IntegrationTests
{
    static int assertions, actions;
    static string RawRequest(string content, bool fragment)
    {
        using (var client = new TcpClient("127.0.0.1", 47931))
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 7000;
            byte[] bytes = Encoding.ASCII.GetBytes(content);
            if (fragment) { for (int i = 0; i < bytes.Length; i += 7) { stream.Write(bytes, i, Math.Min(7, bytes.Length - i)); Thread.Sleep(1); } }
            else stream.Write(bytes, 0, bytes.Length);
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); assertions++; Console.WriteLine("通过：" + message); }
    static string Request(string path, string token, bool post, string origin, out int code)
    {
        var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:47931" + path);
        request.Timeout = 5000;
        if (token != null) request.Headers["X-NightDisplay-Token"] = token;
        if (origin != null) request.Headers["Origin"] = origin;
        if (post) { request.Method = "POST"; request.ContentType = "application/json"; byte[] bytes = Encoding.UTF8.GetBytes("{}"); request.ContentLength = bytes.Length; using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length); }
        HttpWebResponse response;
        try { response = (HttpWebResponse)request.GetResponse(); } catch (WebException ex) { response = (HttpWebResponse)ex.Response; if (response == null) throw; }
        using (response) { code = (int)response.StatusCode; using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd(); }
    }
    static void Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            bool physicalActive = false, deviceDisabled = false;
            Recovery.RestoreOutput(delegate { return physicalActive; }, delegate { physicalActive = true; }, delegate { deviceDisabled = physicalActive; });
            Assert(deviceDisabled, "先恢复并验证实体屏，再关闭虚拟显示设备");
            bool layoutReset = false;
            Recovery.RestoreOutput(delegate { return true; }, delegate { layoutReset = true; }, delegate { });
            Assert(!layoutReset, "通过 Win+P 回到实体屏后保留当前显示布局");
            deviceDisabled = false; bool rejected = false;
            try { Recovery.RestoreOutput(delegate { return false; }, delegate { }, delegate { deviceDisabled = true; }); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && !deviceDisabled, "实体屏恢复验证失败时保留虚拟设备输出");
            bool cleanupFailed = false;
            try { Recovery.RestoreOutput(delegate { return true; }, delegate { }, delegate { throw new IOException("设备关闭失败"); }); }
            catch (IOException) { cleanupFailed = true; }
            Assert(cleanupFailed, "虚拟设备清理失败不会被报告为恢复成功");
            var snapshot = new DisplaySnapshot { Paths = new[] { new PathInfo {
                Source = new PathSource { Adapter = new Luid { High = -3, Low = 123 }, Id = 2, ModeIndex = 0 },
                Target = new PathTarget { Adapter = new Luid { High = -3, Low = 123 }, Id = 5, Available = true,
                    ModeIndex = uint.MaxValue, Refresh = new Rational { Numerator = 60, Denominator = 1 } }, Flags = 1
            } } };
            Assert(System.Runtime.InteropServices.Marshal.SizeOf(typeof(PathInfo)) == 72 && System.Runtime.InteropServices.Marshal.SizeOf(typeof(ModeInfo)) == 64, "显示结构布局符合 Win32 ABI");
            var restored = AppState.Unpack<PathInfo>(AppState.Pack(snapshot.Paths));
            Assert(AppState.Pack(snapshot.Paths) == AppState.Pack(restored), "显示恢复配置可无损序列化");
            Assert(LanServer.LocalAddress(IPAddress.Parse("192.168.1.20")) && !LanServer.LocalAddress(IPAddress.Parse("8.8.8.8")), "网络服务仅接受局域网地址");
            Assert(LanServer.LocalAddress(IPAddress.Parse("172.16.0.1")) && !LanServer.LocalAddress(IPAddress.Parse("172.32.0.1")), "私有网段边界正确");
            Assert(!new Settings().LanEnabled, "新配置默认关闭网页控制");
            using (var owner = new SafetyLease(Guid.NewGuid().ToString("N"))) { owner.Pulse(); Assert(!owner.IsStale, "共享内存心跳有效"); }
            var leaseToken = Guid.NewGuid().ToString("N");
            using (var owner = new SafetyLease(leaseToken))
            using (var observer = new SafetyLease(leaseToken)) {
                owner.Pulse(); Assert(!observer.IsStale, "守护端可读取同一会话的心跳");
                SafetyLease.Notify(leaseToken); Assert(observer.Changed.WaitOne(1000), "配置变更事件可唤醒等待中的守护端");
                Assert(!observer.Changed.WaitOne(0), "配置变更事件消费后自动复位");
                SafetyLease.Publish(new RecoveryState { OwnerToken = leaseToken, VirtualRequested = true, RestoreAfterUtcTicks = 123 });
                using (var memory = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\NightDisplay.Lease." + leaseToken))
                using (var view = memory.CreateViewAccessor())
                    Assert(view.ReadInt64(8) == 1 && view.ReadInt64(16) == 123 && view.ReadInt64(24) == 2, "共享恢复标志与原生协议布局一致");
            }
            var token = Guid.NewGuid().ToString("N");
            using (var server = new LanServer(47931, token, delegate { return new { mode = "physical", displays = new[] { "测试屏幕" } }; }, delegate(string action) { Interlocked.Increment(ref actions); return Task.FromResult<object>(new { mode = action }); }))
            {
                server.Start();
                int code;
                string body = Request("/", null, false, null, out code);
                Assert(code == 200 && body.Contains("让屏幕休息") && !body.Contains(token), "手机页面提供 UTF-8 中文且不泄露访问密钥");
                body = Request("/app.css", null, false, null, out code); Assert(code == 200 && body.Contains("--accent"), "样式资源来自固定嵌入资源");
                body = Request("/app.js", null, false, null, out code); Assert(code == 200 && body.Contains("visibilitychange"), "后台页面暂停轮询");
                Request("/api/status", null, false, null, out code); Assert(code == 401, "无密钥请求被拒绝");
                Request("/api/physical", "wrong", true, null, out code); Assert(code == 401 && actions == 0, "错误密钥不能触发屏幕操作");
                body = Request("/api/status", token, false, null, out code); Assert(code == 200 && body.Contains("测试屏幕"), "正确密钥可以读取真实状态");
                body = RawRequest("GET /api/status HTTP/1.1\r\nHost: 127.0.0.1:47931\r\nX-NightDisplay-Token: " + token + "\r\n\r\n", true);
                Assert(body.StartsWith("HTTP/1.1 200") && body.Contains("测试屏幕"), "分片请求可通过缓冲解析器读取");
                body = RawRequest("GET /api/status HTTP/1.1\r\nHost: evil.example:47931\r\nX-NightDisplay-Token: " + token + "\r\n\r\n", false);
                Assert(body.StartsWith("HTTP/1.1 403"), "无效 Host 不能绕过地址验证");
                body = RawRequest("GET /api/status HTTP/1.1\r\nHost: 127.0.0.1:47931\r\nHost: 127.0.0.1:47931\r\n\r\n", false);
                Assert(body.StartsWith("HTTP/1.1 400"), "重复请求头被拒绝");
                Request("/api/physical", token, true, "http://evil.example", out code); Assert(code == 403 && actions == 0, "跨站请求不能触发控制");
                body = Request("/api/physical", token, true, "http://127.0.0.1:47931", out code); Assert(code == 200 && actions == 1, "授权的恢复操作只触发一次");
                Request("/api/toggle", token, true, null, out code); Assert(code == 429 && actions == 1, "快速重复操作被限流");
                Request("/../src/Program.cs", token, false, null, out code); Assert(code == 404, "网页服务不提供源码和任意文件读取");
                Thread.Sleep(550);
                body = RawRequest("POST /api/physical HTTP/1.1\r\nHost: 127.0.0.1:47931\r\nX-NightDisplay-Token: " + token + "\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}", false);
                Assert(body.StartsWith("HTTP/1.1 200") && actions == 2, "同一 TCP 数据块中的请求头与正文不会丢失");
                body = RawRequest("POST /api/physical HTTP/1.1\r\nHost: 127.0.0.1:47931\r\nX-NightDisplay-Token: " + token + "\r\nContent-Type: application/json\r\nContent-Length: 9999\r\n\r\n", false);
                Assert(body.StartsWith("HTTP/1.1 400") && actions == 2, "超长正文无法触发操作");
                body = RawRequest("GET /", false); Assert(body.Length == 0, "不完整请求在总读取期限后释放连接");
            }
            Console.WriteLine("全部通过，共 " + assertions + " 项。");
        }
        catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
    }
}
