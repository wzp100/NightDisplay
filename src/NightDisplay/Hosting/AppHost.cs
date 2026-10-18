using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NightDisplay
{
    public sealed class AppHost : ApplicationContext
    {
        Settings settings;
        RecoveryState recovery;
        readonly string token = Guid.NewGuid().ToString("N");
        readonly HostWindow window;
        readonly NativeTray tray;
        readonly SafetyLease lease;
        readonly PanelServer panels;
        readonly Timer timer;
        Process guard, panel;
        LanServer lan;
        string lanError = "";
        bool busy, hotkeysReady, virtualOnly, exiting, refreshPending;
        List<DisplayTarget> targets = new List<DisplayTarget>();
        volatile DisplayStatus status;
        public AppHost(bool startup, int testCycle)
        {
            settings = AppState.ReadJson<Settings>(AppState.SettingsPath);
            // Old versions enabled LAN by default; require a new explicit opt-in once.
            if (settings.LanConsentVersion < 2) { settings.LanEnabled = false; settings.LanConsentVersion = 2; }
            AppState.WriteJson(AppState.SettingsPath, settings);
            recovery = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath);
            if (recovery.VirtualRequested || NativeDisplay.IsVirtualOnly()) Recovery.Restore();
            using (var current = Process.GetCurrentProcess()) {
                recovery = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath);
                recovery.OwnerPid = current.Id; recovery.OwnerStartTicks = current.StartTime.ToUniversalTime().Ticks;
                recovery.OwnerToken = token;
            }
            AppState.SetSnapshot(recovery, NativeDisplay.PhysicalSnapshot());
            lease = new SafetyLease(token); lease.Pulse();
            AppState.WriteJson(AppState.RecoveryPath, recovery);
            window = new HostWindow(Receive);
            tray = new NativeTray(window.Handle, TrayAction);
            RegisterKeys(); RefreshTargets(); StartGuard();
            panels = new PanelServer(PanelCommand); panels.Start();
            timer = new Timer { Interval = 3000 };
            timer.Tick += delegate {
                try {
                    lease.Pulse();
                    if (guard == null || guard.HasExited) { RestorePhysical(); StartGuard(); }
                    if (panel != null && panel.HasExited) { panel.Dispose(); panel = null; }
                    if (virtualOnly) RefreshStatus();
                } catch (Exception ex) { AppState.Log("后台心跳：" + ex.Message); }
            };
            timer.Start();
            if (settings.LanEnabled) StartLan();
            if (!startup && testCycle == 0) window.Post(ShowPanel);
            if (testCycle > 0) window.Post(delegate { Run(delegate { SwitchVirtual(testCycle); }); });
        }
        void SaveSettings() { AppState.WriteJson(AppState.SettingsPath, settings); UpdateStatus(); }
        void RegisterKeys()
        {
            NativeDisplay.UnregisterHotKey(window.Handle, 1); NativeDisplay.UnregisterHotKey(window.Handle, 2);
            bool first = NativeDisplay.RegisterHotKey(window.Handle, 1, 3 | 0x4000, settings.ToggleKey);
            bool second = NativeDisplay.RegisterHotKey(window.Handle, 2, 3 | 0x4000, settings.RestoreKey);
            hotkeysReady = first && second;
            AppState.Log("快捷键注册：toggle=" + first + " restore=" + second);
        }
        void StartGuard()
        {
            if (guard != null) guard.Dispose();
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NightDisplay.Guard.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("缺少原生恢复守护程序，已阻止虚拟输出。", exe);
            lease.Ready.Reset();
            guard = GuardProcess.Start(exe, recovery.OwnerPid + " " + recovery.OwnerStartTicks + " " + token);
            if (!lease.Ready.WaitOne(5000) || guard.HasExited) throw new InvalidOperationException("原生恢复守护没有就绪。");
            File.WriteAllText(Path.Combine(AppState.DirectoryPath, "watch-ready.txt"), token + "|" + guard.Id, AppState.Utf8);
        }
        void RefreshTargets()
        {
            targets = NativeDisplay.Targets();
            var selected = targets.FirstOrDefault(t => t.Virtual && string.Equals(t.DevicePath, settings.VirtualDevicePath, StringComparison.OrdinalIgnoreCase))
                ?? targets.FirstOrDefault(t => t.Virtual);
            if (selected != null && settings.VirtualDevicePath != selected.DevicePath) { settings.VirtualDevicePath = selected.DevicePath; SaveSettings(); }
            RefreshStatus();
        }
        void RefreshStatus()
        {
            var active = NativeDisplay.Query(false).Paths.Select(NativeDisplay.Describe).ToArray();
            bool isVirtual = active.Length > 0 && active.All(t => t.Virtual);
            if (virtualOnly && !isVirtual) {
                recovery = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath);
                recovery.VirtualRequested = false; recovery.RestoreAfterUtcTicks = 0;
                AppState.WriteJson(AppState.RecoveryPath, recovery);
            }
            virtualOnly = isVirtual;
            NativeDisplay.SetThreadExecutionState(0x80000000u | (isVirtual && settings.KeepAwake ? 3u : 0u));
            status = new DisplayStatus { Mode = active.Length == 0 ? "unknown" : isVirtual ? "virtual" : "physical",
                Displays = active.Select(t => t.Name).ToArray(), VirtualAvailable = targets.Any(t => t.Virtual),
                Hotkey = "Ctrl + Alt + " + ((Keys)settings.ToggleKey), RestoreHotkey = "Ctrl + Alt + " + ((Keys)settings.RestoreKey),
                RestoreAfterUtcTicks = recovery.RestoreAfterUtcTicks };
            tray.SetText("NightDisplay · " + (isVirtual ? "虚拟屏" : "实体屏"));
        }
        void UpdateStatus()
        {
            var previous = status;
            if (previous == null) return;
            status = new DisplayStatus { Mode = previous.Mode, Displays = previous.Displays,
                VirtualAvailable = targets.Any(t => t.Virtual), Hotkey = "Ctrl + Alt + " + ((Keys)settings.ToggleKey),
                RestoreHotkey = "Ctrl + Alt + " + ((Keys)settings.RestoreKey), RestoreAfterUtcTicks = recovery.RestoreAfterUtcTicks };
        }
        void SwitchVirtual(int seconds)
        {
            if (!hotkeysReady) throw new InvalidOperationException("请先设置可用的切换与恢复快捷键。");
            if (guard == null || guard.HasExited) throw new InvalidOperationException("恢复守护未运行，已阻止进入虚拟输出。");
            if (NativeDisplay.IsVirtualOnly()) { if (seconds > 0) throw new InvalidOperationException("请先恢复实体屏，再进行测试。"); return; }
            RefreshTargets();
            var selected = targets.FirstOrDefault(t => t.Virtual && t.DevicePath == settings.VirtualDevicePath);
            lease.Pulse(); DisplaySwitchService.EnterVirtual(selected, recovery, seconds); RefreshStatus();
        }
        void RestorePhysical() { Recovery.Restore(); recovery = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath); RefreshTargets(); }
        void Toggle() { if (NativeDisplay.IsVirtualOnly()) RestorePhysical(); else SwitchVirtual(0); }
        void ShowPanel()
        {
            if (panel != null && !panel.HasExited) {
                NativeDisplay.PostMessage(new IntPtr(0xffff), Program.PanelShowMessage, IntPtr.Zero, IntPtr.Zero); return;
            }
            if (panel != null) panel.Dispose();
            panel = Process.Start(new ProcessStartInfo(Program.Exe, "--panel") { UseShellExecute = false });
        }
        void StartLan()
        {
            StopLan(); lanError = "";
            if (!settings.LanEnabled) return;
            try {
                if (string.IsNullOrWhiteSpace(settings.LanToken)) {
                    byte[] bytes = new byte[24]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                    settings.LanToken = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); SaveSettings();
                }
                lan = new LanServer(settings.LanPort, settings.LanToken, delegate { return status.ToResponse(); }, PhoneCommand);
                lan.Start();
                File.WriteAllText(Path.Combine(AppState.DirectoryPath, "phone-link.txt"), lan.PhoneUrl, AppState.Utf8);
            } catch (Exception ex) { StopLan(); lanError = ex.Message; settings.LanEnabled = false; SaveSettings(); AppState.Log("网页服务启动失败：" + ex.Message); throw; }
        }
        void StopLan() { if (lan != null) { lan.Dispose(); lan = null; } }
        Task<object> PhoneCommand(string action)
        {
            var task = new TaskCompletionSource<object>();
            window.Post(delegate { try { Execute(action); task.SetResult(status.ToResponse()); } catch (Exception ex) { task.SetException(ex); } });
            return task.Task;
        }
        void Execute(string action)
        {
            if (busy) throw new InvalidOperationException("正在切换，请稍后重试。");
            busy = true;
            try {
                switch (action) {
                    case "virtual": SwitchVirtual(0); break; case "physical": RestorePhysical(); break;
                    case "toggle": Toggle(); break; case "test": SwitchVirtual(8); break;
                    default: throw new InvalidOperationException("未知操作。");
                }
            } finally { busy = false; }
        }
        PanelSnapshot Snapshot()
        {
            // Copy settings to prevent cross-thread serialization of mutable state.
            return new PanelSnapshot { Settings = new Settings { VirtualDevicePath = settings.VirtualDevicePath,
                Modifiers = settings.Modifiers, ToggleKey = settings.ToggleKey, RestoreKey = settings.RestoreKey,
                KeepAwake = settings.KeepAwake, LanEnabled = settings.LanEnabled, LanConsentVersion = settings.LanConsentVersion,
                LanPort = settings.LanPort, LanToken = settings.LanToken }, Targets = targets.Where(t => t.Virtual).ToArray(),
                Mode = status.Mode, Detail = string.Join(" / ", status.Displays), HotkeysReady = hotkeysReady,
                LanRunning = lan != null, PhoneUrl = lan == null ? "" : lan.PhoneUrl, LocalUrl = lan == null ? "" : lan.LocalUrl,
                PhoneBaseUrl = lan == null ? "" : lan.PhoneBaseUrl, LanError = lanError, Startup = AppState.StartupEnabled(),
                PanelRunning = panel != null && !panel.HasExited, HostPid = recovery.OwnerPid, GuardPid = guard == null ? 0 : guard.Id };
        }
        Task<PanelSnapshot> PanelCommand(PanelRequest request)
        {
            var task = new TaskCompletionSource<PanelSnapshot>();
            window.Post(delegate {
                try {
                    switch (request.Action) {
                        case "status": break;
                        case "show": ShowPanel(); break;
                        case "refresh": RefreshTargets(); break;
                        case "virtual": case "physical": case "toggle": case "test": Execute(request.Action); break;
                        case "target": settings.VirtualDevicePath = request.DevicePath; SaveSettings(); break;
                        case "awake": settings.KeepAwake = request.Enabled; SaveSettings(); RefreshStatus(); break;
                        case "startup": AppState.SetStartup(request.Enabled, Program.Exe); break;
                        case "lan": settings.LanEnabled = request.Enabled; settings.LanConsentVersion = 2; SaveSettings(); if (request.Enabled) StartLan(); else StopLan(); break;
                        case "hotkeys":
                            if (request.ToggleKey == request.RestoreKey || request.ToggleKey < 0x70 || request.ToggleKey > 0x7b || request.RestoreKey < 0x70 || request.RestoreKey > 0x7b) throw new InvalidOperationException("请设置两个不同的 F 键。");
                            uint previousToggle = settings.ToggleKey, previousRestore = settings.RestoreKey;
                            settings.ToggleKey = request.ToggleKey; settings.RestoreKey = request.RestoreKey; RegisterKeys();
                            if (!hotkeysReady) { settings.ToggleKey = previousToggle; settings.RestoreKey = previousRestore; RegisterKeys(); throw new InvalidOperationException("新快捷键被占用，已恢复原快捷键。"); }
                            SaveSettings(); break;
                        default: throw new InvalidOperationException("未知本地控制操作。");
                    }
                    task.SetResult(Snapshot());
                } catch (Exception ex) { task.SetException(ex); }
            });
            return task.Task;
        }
        void TrayAction(int selected) { if (selected == 1) ShowPanel(); else if (selected == 2) Run(Toggle); else if (selected == 3) Run(RestorePhysical); else if (selected == 4) Run(Quit); }
        void Run(Action action) { if (busy) return; busy = true; try { action(); } catch (Exception ex) { AppState.Log(ex.ToString()); ShowPanel(); } finally { busy = false; } }
        void Receive(int message, IntPtr wParam, IntPtr lParam)
        {
            if (tray != null) tray.Receive(message, lParam);
            if (message == 0x7e && !refreshPending && !exiting) {
                refreshPending = true; window.Post(delegate { refreshPending = false; if (!busy) Run(RefreshTargets); });
            }
            if (message == 0x312) { if (wParam.ToInt32() == 1) Run(Toggle); if (wParam.ToInt32() == 2) Run(RestorePhysical); }
            if (message == Program.RestoreMessage) Run(RestorePhysical);
            if (message == Program.ToggleMessage) Run(Toggle);
            if (message == Program.ShowMessage) ShowPanel();
            if (message == Program.BackgroundMessage) NativeDisplay.PostMessage(new IntPtr(0xffff), Program.PanelCloseMessage, IntPtr.Zero, IntPtr.Zero);
            if (message == Program.RestoredMessage) Run(delegate { recovery = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath); RefreshTargets(); });
            if (message == Program.QuitMessage) Run(Quit);
        }
        void Quit() { RestorePhysical(); exiting = true; ExitThread(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                NativeDisplay.PostMessage(new IntPtr(0xffff), Program.PanelCloseMessage, IntPtr.Zero, IntPtr.Zero);
                if (timer != null) timer.Dispose(); if (panels != null) panels.Dispose(); StopLan();
                if (window != null) { NativeDisplay.UnregisterHotKey(window.Handle, 1); NativeDisplay.UnregisterHotKey(window.Handle, 2); }
                NativeDisplay.SetThreadExecutionState(0x80000000);
                if (tray != null) tray.Dispose(); if (window != null) window.Dispose();
                if (lease != null) lease.Dispose(); if (guard != null) guard.Dispose(); if (panel != null) panel.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
