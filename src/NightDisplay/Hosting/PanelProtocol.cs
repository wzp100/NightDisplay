using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NightDisplay
{
    public sealed class PanelRequest
    {
        public string Action = "status", DevicePath = "";
        public bool Enabled;
        public uint ToggleKey, RestoreKey;
    }
    public sealed class PanelSnapshot
    {
        public Settings Settings;
        public DisplayTarget[] Targets;
        public string Mode, Detail, PhoneUrl, LocalUrl, PhoneBaseUrl, LanError;
        public bool HotkeysReady, LanRunning, Startup, PanelRunning;
        public int HostPid, GuardPid;
    }
    public sealed class PanelResponse { public PanelSnapshot State; public string Error; }
    public static class PanelProtocol
    {
        public static string PipeName { get { return "NightDisplay.Panel." + WindowsIdentity.GetCurrent().User.Value; } }
        public static async Task<PanelSnapshot> Send(PanelRequest request)
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (var deadline = new System.Threading.Timer(delegate { pipe.Dispose(); }, null, 15000, Timeout.Infinite))
            {
                await pipe.ConnectAsync(2500).ConfigureAwait(false);
                var serializer = new JavaScriptSerializer();
                using (var writer = new StreamWriter(pipe, AppState.Utf8, 4096, true))
                using (var reader = new StreamReader(pipe, AppState.Utf8, false, 4096, true))
                {
                    await writer.WriteLineAsync(serializer.Serialize(request)).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    string line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) throw new IOException("后台控制进程已断开。");
                    var response = serializer.Deserialize<PanelResponse>(line);
                    if (!string.IsNullOrEmpty(response.Error)) throw new InvalidOperationException(response.Error);
                    return response.State;
                }
            }
        }
    }
    public sealed class PanelServer : IDisposable
    {
        readonly Func<PanelRequest, Task<PanelSnapshot>> handle;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly object gate = new object();
        NamedPipeServerStream active;
        public PanelServer(Func<PanelRequest, Task<PanelSnapshot>> handle) { this.handle = handle; }
        public void Start() { Task.Run(new Func<Task>(Loop)); }
        async Task Loop()
        {
            while (!cancellation.IsCancellationRequested)
            {
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                using (var pipe = new NamedPipeServerStream(PanelProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                {
                    lock (gate) { if (cancellation.IsCancellationRequested) return; active = pipe; }
                    try {
                        await pipe.WaitForConnectionAsync(cancellation.Token).ConfigureAwait(false);
                        using (var timeout = new System.Threading.Timer(delegate { pipe.Dispose(); }, null, 15000, Timeout.Infinite))
                        using (var reader = new StreamReader(pipe, AppState.Utf8, false, 4096, true))
                        using (var writer = new StreamWriter(pipe, AppState.Utf8, 4096, true))
                        {
                            // Fixed message size, even though only the current user's SID can connect.
                            char[] buffer = new char[16384]; int count = 0;
                            while (count < buffer.Length) {
                                int n = await reader.ReadAsync(buffer, count, 1).ConfigureAwait(false);
                                if (n == 0) throw new IOException("本地控制连接已关闭。");
                                if (buffer[count] == '\n') break;
                                count++;
                            }
                            if (count == buffer.Length) throw new IOException("本地控制请求过长。");
                            var serializer = new JavaScriptSerializer();
                            var request = serializer.Deserialize<PanelRequest>(new string(buffer, 0, count));
                            var response = new PanelResponse();
                            try { response.State = await handle(request).ConfigureAwait(false); }
                            catch (Exception ex) { response.Error = ex.Message; }
                            await writer.WriteLineAsync(serializer.Serialize(response)).ConfigureAwait(false);
                            await writer.FlushAsync().ConfigureAwait(false);
                        }
                    } catch (Exception ex) { if (!cancellation.IsCancellationRequested) AppState.Log("本地控制连接：" + ex.GetType().Name); }
                    finally { lock (gate) active = null; }
                }
            }
        }
        public void Dispose() { cancellation.Cancel(); lock (gate) if (active != null) active.Dispose(); }
    }
}
