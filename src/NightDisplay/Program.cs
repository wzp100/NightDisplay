using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NightDisplay
{
    public static class Program
    {
        public static readonly uint ShowMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Show.v1");
        public static readonly uint RestoreMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Restore.v1");
        public static readonly uint ToggleMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Toggle.v1");
        public static readonly uint RestoredMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Restored.v1");
        public static readonly uint QuitMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Quit.v1");
        public static readonly uint PanelShowMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Panel.Show.v2");
        public static readonly uint BackgroundMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Background.v2");
        public static readonly uint PanelCloseMessage = NativeDisplay.RegisterWindowMessage("NightDisplay.Panel.Close.v2");
        public static string Exe { get { return Application.ExecutablePath; } }
        [STAThread]
        public static int Main(string[] args)
        {
            // Scheduled SYSTEM actions must not initialize user UI, recovery files or display APIs.
            if (args.Length == 2 && (args[0] == "--device-enable" || args[0] == "--device-disable")) {
                try {
                    if (!DeviceControlTasks.IsAdministrator()) return 1;
                    VirtualDisplayDevice.ChangeState(args[1], args[0] == "--device-enable"); return 0;
                } catch { return 1; }
            }
            Directory.CreateDirectory(AppState.DirectoryPath);
            if (args.Contains("--configure-device-control") || args.Contains("--remove-device-control")) {
                try {
                    if (args.Contains("--remove-device-control")) DeviceControlTasks.Remove(); else DeviceControlTasks.Configure();
                    return 0;
                } catch (Exception ex) { AppState.Log(ex.ToString()); return 1; }
            }
            if (args.Contains("--panel")) {
                bool panelCreated;
                using (var panelMutex = new Mutex(true, @"Local\NightDisplay.Panel.v2", out panelCreated)) {
                    if (!panelCreated) { NativeDisplay.PostMessage(new IntPtr(0xffff), PanelShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    // WPF is loaded only here, inside the on-demand settings process.
                    return PanelApp.Run();
                }
            }
            if (args.Contains("--configure-lan")) {
                try { DriverInstaller.ConfigureFirewall(); return 0; }
                catch (Exception ex) { AppState.Log(ex.ToString()); return 1; }
            }
            if (args.Contains("--diagnose") || args.Contains("--self-test")) return Diagnostics(args.Contains("--self-test"));
            if (args.Contains("--install-driver") || args.Contains("--setup"))
            {
                try {
                    var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                    if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                    {
                        using (var elevated = Process.Start(new ProcessStartInfo(Exe, args.Contains("--setup") ? "--setup" : "--install-driver") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) { elevated.WaitForExit(); return elevated.ExitCode; }
                    }
                    if (!DriverInstaller.Installed() && !NativeDisplay.Targets().Any(t => t.Virtual)) DriverInstaller.Install(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver"));
                    if (DriverInstaller.Installed() && DeviceControlTasks.SupportsTasks) DeviceControlTasks.Configure();
                    // Driver setup does not start or configure the optional LAN feature.
                    return 0;
                }
                catch (Exception ex) { AppState.Log(ex.ToString()); File.WriteAllText(Path.Combine(AppState.DirectoryPath, "driver-install-result.txt"), "安装失败：" + ex.Message, AppState.Utf8); MessageBox.Show(ex.Message, "虚拟屏安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            if (args.Contains("--restore"))
            {
                try { string expected = args.Contains("--restore-for") ? args[Array.IndexOf(args, "--restore-for") + 1] : null; if (!Recovery.Restore(expected)) return 2; NativeDisplay.PostMessage(new IntPtr(0xffff), RestoredMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                catch (Exception ex) { AppState.Log(ex.ToString()); if (!args.Contains("--headless")) MessageBox.Show(ex.Message, "恢复实体屏", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\NightDisplay.Main.v1", out created))
            {
                if (!created)
                {
                    uint message = args.Contains("--quit") ? QuitMessage : args.Contains("--background") ? BackgroundMessage : args.Contains("--toggle") ? ToggleMessage : ShowMessage;
                    NativeDisplay.PostMessage(new IntPtr(0xffff), message, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                if (args.Contains("--quit")) return 0;
                NativeDisplay.SetProcessDPIAware();
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { AppState.Log(e.Exception.ToString()); try { Recovery.Restore(); } catch { } MessageBox.Show(e.Exception.Message, "NightDisplay", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                try { using (var host = new AppHost(args.Contains("--startup"), args.Contains("--test-cycle") ? int.Parse(args[Array.IndexOf(args, "--test-cycle") + 1]) : 0)) Application.Run(host); }
                catch (Exception ex) { AppState.Log(ex.ToString()); try { Recovery.Restore(); } catch { } MessageBox.Show(ex.Message, "NightDisplay", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            return 0;
        }
        static int Diagnostics(bool selfTest)
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add("NightDisplay " + (selfTest ? "自检" : "显示器诊断") + " · " + DateTime.Now);
                foreach (var target in NativeDisplay.Targets()) lines.Add(target.ToString() + " | tech=" + target.Technology + " | " + target.DevicePath);
                var current = NativeDisplay.Query(false);
                lines.Add("活动显示路径：" + current.Paths.Length);
                if (selfTest)
                {
                    if (System.Runtime.InteropServices.Marshal.SizeOf(typeof(PathInfo)) != 72 || System.Runtime.InteropServices.Marshal.SizeOf(typeof(ModeInfo)) != 64) throw new Exception("Win32 结构长度错误。");
                    var roundtrip = AppState.Unpack<PathInfo>(AppState.Pack(current.Paths));
                    if (AppState.Pack(roundtrip) != AppState.Pack(current.Paths)) throw new Exception("显示路径序列化校验失败。");
                    NativeDisplay.Check(NativeDisplay.SetDisplayConfig((uint)current.Paths.Length, current.Paths, (uint)current.Modes.Length, current.Modes, NativeDisplay.Validate | NativeDisplay.Supplied | NativeDisplay.AllowChanges), "验证当前配置");
                    foreach (var v in NativeDisplay.Targets().Where(t => t.Virtual)) { NativeDisplay.ValidateVirtual(v); lines.Add("虚拟屏配置验证通过：" + v.Name); }
                    DriverInstaller.VerifyPackage(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver"));
                    lines.Add("结构、恢复配置序列化、Windows 显示配置验证、驱动 SHA-256 校验：通过。");
                }
                File.WriteAllLines(Path.Combine(AppState.DirectoryPath, selfTest ? "self-test.txt" : "diagnostics.txt"), lines, AppState.Utf8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppState.DirectoryPath, "self-test-error.txt"), ex.ToString(), AppState.Utf8); return 1; }
        }
    }
}
