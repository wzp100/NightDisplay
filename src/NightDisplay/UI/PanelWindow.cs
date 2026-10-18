using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NightDisplay
{
    // Entry point kept free of WPF types in its signature so the background process never loads WPF.
    public static class PanelApp
    {
        public static int Run()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Theme.Install(app.Resources, null);
            return app.Run(new PanelWindow());
        }
    }

    // This process is created only when the user opens settings and exits when the window closes.
    public sealed partial class PanelWindow : Window
    {
        enum Notice { Error, Warning, Success }
        Settings settings = new Settings();
        PanelSnapshot snapshot;
        readonly FrameworkElement root;
        readonly ComboBox virtualSelect, toggleSelect, restoreSelect;
        readonly CheckBox startupCheck, awakeCheck, lanCheck;
        readonly TextBlock stateText, detailText, stateGlyph, hotkeyText, phoneText, infoGlyph, infoTitle, infoText;
        readonly Border stateBadge, infoBar;
        readonly Button virtualButton, testButton, installButton, phoneCopyButton, phoneOpenButton;
        readonly FrameworkElement homePage, settingsPage;
        DispatcherTimer refresh;
        bool busy, polling, initialized, refreshingTargets;
        string targetSignature = "";

        public PanelWindow() : this(false) { }
        PanelWindow(bool preview)
        {
            Title = "NightDisplay · 夜间屏幕控制";
            Width = 880; Height = 640; MinWidth = 760; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = Theme.Image("NightDisplay.Icons.nightdisplay.ico");
            SetResourceReference(BackgroundProperty, "WindowBackground");
            root = (FrameworkElement)Theme.Load("Panel.xaml");
            Content = root;
            Find<Image>("Logo").Source = Theme.Image("NightDisplay.Web.icon.png");
            virtualSelect = Find<ComboBox>("VirtualSelect"); toggleSelect = Find<ComboBox>("ToggleSelect"); restoreSelect = Find<ComboBox>("RestoreSelect");
            startupCheck = Find<CheckBox>("StartupCheck"); awakeCheck = Find<CheckBox>("AwakeCheck"); lanCheck = Find<CheckBox>("LanCheck");
            stateText = Find<TextBlock>("StateText"); detailText = Find<TextBlock>("DetailText"); stateGlyph = Find<TextBlock>("StateGlyph");
            hotkeyText = Find<TextBlock>("HotkeyText"); phoneText = Find<TextBlock>("PhoneText");
            infoGlyph = Find<TextBlock>("InfoGlyph"); infoTitle = Find<TextBlock>("InfoTitle"); infoText = Find<TextBlock>("InfoText");
            stateBadge = Find<Border>("StateBadge"); infoBar = Find<Border>("InfoBar");
            virtualButton = Find<Button>("VirtualButton"); testButton = Find<Button>("TestButton"); installButton = Find<Button>("InstallButton");
            phoneCopyButton = Find<Button>("PhoneCopyButton"); phoneOpenButton = Find<Button>("PhoneOpenButton");
            homePage = Find<FrameworkElement>("HomePage"); settingsPage = Find<FrameworkElement>("SettingsPage");

            for (int i = 1; i <= 12; i++) { toggleSelect.Items.Add("F" + i); restoreSelect.Items.Add("F" + i); }
            toggleSelect.SelectedIndex = KeyIndex(settings.ToggleKey); restoreSelect.SelectedIndex = KeyIndex(settings.RestoreKey);
            Find<RadioButton>("NavHome").Checked += delegate { ShowPage(true); };
            Find<RadioButton>("NavSettings").Checked += delegate { ShowPage(false); };
            Find<Button>("InfoClose").Click += delegate { infoBar.Visibility = Visibility.Collapsed; };
            virtualButton.Click += delegate { Run(new PanelRequest { Action = "virtual" }); };
            Find<Button>("PhysicalButton").Click += delegate { Run(new PanelRequest { Action = "physical" }); };
            testButton.Click += delegate { Run(new PanelRequest { Action = "test" }); };
            Find<Button>("RefreshButton").Click += delegate { Run(new PanelRequest { Action = "refresh" }); };
            Find<Button>("SaveHotkeysButton").Click += delegate {
                Run(new PanelRequest { Action = "hotkeys", ToggleKey = (uint)(0x70 + toggleSelect.SelectedIndex), RestoreKey = (uint)(0x70 + restoreSelect.SelectedIndex) });
            };
            installButton.Click += delegate { InstallDriver(); };
            // Click fires only for user input, so applying a snapshot never echoes a toggle back to the host.
            startupCheck.Click += delegate { Run(new PanelRequest { Action = "startup", Enabled = startupCheck.IsChecked == true }); };
            awakeCheck.Click += delegate { Run(new PanelRequest { Action = "awake", Enabled = awakeCheck.IsChecked == true }); };
            lanCheck.Click += delegate { SetLan(lanCheck.IsChecked == true); };
            virtualSelect.SelectionChanged += delegate {
                var target = virtualSelect.SelectedItem as DisplayTarget;
                if (!refreshingTargets && target != null && settings.VirtualDevicePath != target.DevicePath) {
                    settings.VirtualDevicePath = target.DevicePath;
                    Run(new PanelRequest { Action = "target", DevicePath = target.DevicePath });
                }
            };
            phoneCopyButton.Click += delegate {
                if (snapshot == null || !snapshot.LanRunning) return;
                try { Clipboard.SetText(snapshot.PhoneUrl); phoneText.Text = "手机链接已复制。手机与电脑需连接同一局域网。"; }
                catch (Exception ex) { ShowNotice(Notice.Warning, "未能复制链接", ex.Message); }
            };
            phoneOpenButton.Click += delegate { if (snapshot != null && snapshot.LanRunning) Process.Start(snapshot.LocalUrl); };
            if (preview) return;

            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refresh.Tick += async delegate { await RefreshPanel(); };
            SourceInitialized += delegate {
                Theme.Backdrop(this);
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WndProc);
            };
            ContentRendered += async delegate { await RefreshPanel(); refresh.Start(); };
            StateChanged += delegate { if (WindowState == WindowState.Minimized) Close(); };
            SystemEvents.UserPreferenceChanged += ThemeChanged;
            Closed += delegate { refresh.Stop(); SystemEvents.UserPreferenceChanged -= ThemeChanged; };
        }
        T Find<T>(string name) where T : class { return (T)root.FindName(name); }
        static int KeyIndex(uint key) { return Math.Max(0, Math.Min(11, (int)key - 0x70)); }
        void ShowPage(bool home)
        {
            if (homePage == null) return;
            homePage.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            settingsPage.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        }
        void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Dispatcher.BeginInvoke(new Action(delegate {
                bool dark = Theme.SystemDark();
                if (dark == Theme.Dark) return;
                Theme.Apply(Application.Current.Resources, dark); Theme.Backdrop(this);
            }));
        }
        async Task RefreshPanel()
        {
            if (busy || polling || !IsLoaded) return;
            polling = true;
            try { var state = await PanelProtocol.Send(new PanelRequest()); if (IsLoaded) Apply(state); }
            catch (Exception ex) { if (IsLoaded) { stateText.Text = "后台暂未响应"; detailText.Text = ex.Message; } }
            finally { polling = false; }
        }
        void Apply(PanelSnapshot state)
        {
            snapshot = state; settings = state.Settings; refreshingTargets = true;
            try {
                bool virtualMode = state.Mode == "virtual";
                stateText.Text = virtualMode ? "当前：仅虚拟屏输出" : state.Mode == "physical" ? "当前：实体屏可见" : "未检测到活动输出";
                detailText.Text = state.Detail + (virtualMode ? "\n可用恢复快捷键或手机控制页唤回实体屏。" : "\n桌面与后台任务保持运行。");
                stateBadge.SetResourceReference(Border.BackgroundProperty, virtualMode ? "Accent" : "SubtleFillSelected");
                stateGlyph.SetResourceReference(TextBlock.ForegroundProperty, virtualMode ? "OnAccent" : "TextPrimary");
                // Segoe Fluent Icons: QuietHours (moon) while only the virtual display outputs, TVMonitor otherwise.
                stateGlyph.Text = virtualMode ? "" : "";
                string signature = string.Join("|", state.Targets.Select(t => t.DevicePath + t.Name));
                if (!initialized || signature != targetSignature) {
                    virtualSelect.Items.Clear();
                    foreach (var target in state.Targets) virtualSelect.Items.Add(target);
                    targetSignature = signature;
                }
                var selected = virtualSelect.Items.OfType<DisplayTarget>().FirstOrDefault(t => t.DevicePath == settings.VirtualDevicePath);
                if (selected != null) virtualSelect.SelectedItem = selected;
                if (!initialized) { toggleSelect.SelectedIndex = KeyIndex(settings.ToggleKey); restoreSelect.SelectedIndex = KeyIndex(settings.RestoreKey); }
                hotkeyText.Text = state.HotkeysReady ? "快捷键已启用；关闭此窗口后仍可切换与恢复。" : "快捷键被占用，请重新设置。";
                hotkeyText.SetResourceReference(TextBlock.ForegroundProperty, state.HotkeysReady ? "TextSecondary" : "Danger");
                startupCheck.IsChecked = state.Startup; awakeCheck.IsChecked = settings.KeepAwake; lanCheck.IsChecked = settings.LanEnabled;
                bool available = state.Targets.Length > 0;
                virtualButton.IsEnabled = available && state.HotkeysReady && !virtualMode;
                testButton.IsEnabled = virtualButton.IsEnabled; installButton.IsEnabled = !available;
                phoneCopyButton.IsEnabled = phoneOpenButton.IsEnabled = state.LanRunning;
                phoneText.Text = state.LanRunning ? "手机地址：" + state.PhoneBaseUrl + "\n复制完整访问链接，用手机打开。" :
                    string.IsNullOrEmpty(state.LanError) ? "网页控制已关闭。需要时在“设置与保护”中开启。" : "网页控制未启动：" + state.LanError;
                initialized = true;
            } finally { refreshingTargets = false; }
        }
        void ShowNotice(Notice kind, string title, string message)
        {
            infoTitle.Text = title; infoText.Text = message;
            // Segoe Fluent Icons: ErrorBadge, Warning, Completed.
            infoGlyph.Text = kind == Notice.Error ? "" : kind == Notice.Warning ? "" : "";
            infoGlyph.SetResourceReference(TextBlock.ForegroundProperty, kind == Notice.Error ? "Danger" : kind == Notice.Success ? "Accent" : "TextPrimary");
            infoBar.SetResourceReference(Border.BackgroundProperty, kind == Notice.Error ? "InfoError" : kind == Notice.Warning ? "InfoWarning" : "InfoSuccess");
            infoBar.Visibility = Visibility.Visible;
        }
        async void Run(PanelRequest request)
        {
            if (busy || refreshingTargets) return;
            busy = true;
            try { Apply(await PanelProtocol.Send(request)); infoBar.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { ShowNotice(Notice.Error, "操作未完成", ex.Message); }
            finally { busy = false; }
        }
        async void SetLan(bool enabled)
        {
            if (busy) return;
            busy = true;
            try {
                Apply(await PanelProtocol.Send(new PanelRequest { Action = "lan", Enabled = enabled }));
                if (enabled) {
                    // LAN permission is configured only after the user selects this feature.
                    using (var process = Process.Start(new ProcessStartInfo(Program.Exe, "--configure-lan") {
                        UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                    })) { await Task.Run(delegate { process.WaitForExit(); }); if (process.ExitCode != 0) throw new InvalidOperationException("Windows 未完成局域网访问配置，网页控制已关闭。请重新开启后重试。"); }
                }
                infoBar.Visibility = Visibility.Collapsed;
            } catch (Exception ex) {
                if (enabled) { try { Apply(PanelProtocol.Send(new PanelRequest { Action = "lan", Enabled = false }).GetAwaiter().GetResult()); } catch { } }
                ShowNotice(Notice.Warning, "手机控制", ex.Message);
            }
            finally { busy = false; }
        }
        async void InstallDriver()
        {
            if (busy) return; busy = true; installButton.IsEnabled = false;
            try {
                using (var process = Process.Start(new ProcessStartInfo(Program.Exe, "--setup") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) {
                    await Task.Run(delegate { process.WaitForExit(); });
                    if (process.ExitCode != 0) throw new InvalidOperationException("虚拟屏未完成安装，请查看日志。");
                }
                await Task.Delay(1800); Apply(await PanelProtocol.Send(new PanelRequest { Action = "refresh" }));
                ShowNotice(Notice.Success, "安装虚拟屏", "虚拟显示器已安装，可以回到屏幕控制先测试 8 秒。");
            } catch (Exception ex) { ShowNotice(Notice.Error, "安装虚拟屏", ex.Message); }
            finally { busy = false; installButton.IsEnabled = virtualSelect.Items.Count == 0; }
        }
        IntPtr WndProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)message == Program.PanelShowMessage) { Show(); WindowState = WindowState.Normal; Activate(); }
            if ((uint)message == Program.PanelCloseMessage) Dispatcher.BeginInvoke(new Action(Close));
            return IntPtr.Zero;
        }
    }
}
