using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NightDisplay
{
    // Fluent palette, XAML loading and the Windows 11 Mica backdrop. Loaded only by the settings process.
    public static class Theme
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        const int DarkTitleBar = 20, CornerPreference = 33, SystemBackdrop = 38, MicaBackdrop = 2, RoundCorners = 2;

        public static bool Dark { get; private set; }
        public static bool Mica { get; private set; }

        public static object Load(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NightDisplay.UI." + name))
            using (var reader = new StreamReader(stream, AppState.Utf8)) return XamlReader.Parse(reader.ReadToEnd());
        }
        public static BitmapFrame Image(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        public static void Install(ResourceDictionary resources, bool? forceDark)
        {
            resources.MergedDictionaries.Add((ResourceDictionary)Load("Theme.xaml"));
            Apply(resources, forceDark ?? SystemDark());
        }
        public static void Apply(ResourceDictionary resources, bool dark)
        {
            Dark = dark;
            // Brand teal from the phone page; card fills stay translucent so Mica shows through.
            Set(resources, "Accent", dark ? "#85E6C6" : "#177868");
            Set(resources, "AccentHover", dark ? "#9CEBD1" : "#1F8574");
            Set(resources, "AccentPressed", dark ? "#6FCFB0" : "#136456");
            Set(resources, "OnAccent", dark ? "#0F3329" : "#FFFFFF");
            Set(resources, "TextPrimary", dark ? "#FFFFFF" : "#162B35");
            Set(resources, "TextSecondary", dark ? "#C2D1CF" : "#5B6F77");
            Set(resources, "TextDisabled", dark ? "#7FFFFFFF" : "#5C000000");
            Set(resources, "Danger", dark ? "#FF99A4" : "#C42B1C");
            Set(resources, "WindowBackground", dark ? "#1E2629" : "#EEF2F3");
            Set(resources, "CardBackground", dark ? "#0DFFFFFF" : "#B3FFFFFF");
            Set(resources, "CardStroke", dark ? "#1A000000" : "#0F000000");
            Set(resources, "ControlFill", dark ? "#0FFFFFFF" : "#B3FFFFFF");
            Set(resources, "ControlFillHover", dark ? "#15FFFFFF" : "#80F9F9F9");
            Set(resources, "ControlFillPressed", dark ? "#08FFFFFF" : "#4DF9F9F9");
            Set(resources, "ControlFillStrong", dark ? "#00000000" : "#05000000");
            Set(resources, "ControlFillDisabled", dark ? "#28FFFFFF" : "#37000000");
            Set(resources, "ControlStroke", dark ? "#18FFFFFF" : "#1A000000");
            Set(resources, "SubtleFillHover", dark ? "#0FFFFFFF" : "#09000000");
            Set(resources, "SubtleFillSelected", dark ? "#15FFFFFF" : "#0F000000");
            Set(resources, "FlyoutBackground", dark ? "#2C2C2C" : "#F9F9F9");
            Set(resources, "FlyoutStroke", dark ? "#33000000" : "#0F000000");
            Set(resources, "InfoError", dark ? "#442726" : "#FDE7E9");
            Set(resources, "InfoWarning", dark ? "#433519" : "#FFF4CE");
            Set(resources, "InfoSuccess", dark ? "#393D1B" : "#DFF6DD");
        }
        static void Set(ResourceDictionary resources, string key, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            resources[key] = brush;
        }
        public static bool SystemDark()
        {
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && Equals(key.GetValue("AppsUseLightTheme"), 0);
            } catch { return false; }
        }
        // Windows 11 22H2+ draws Mica behind a transparent client area; older systems keep a solid background.
        public static void Backdrop(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            int dark = Dark ? 1 : 0, corners = RoundCorners, mica = MicaBackdrop;
            DwmSetWindowAttribute(handle, DarkTitleBar, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, CornerPreference, ref corners, sizeof(int));
            Mica = DwmSetWindowAttribute(handle, SystemBackdrop, ref mica, sizeof(int)) == 0;
            if (Mica) {
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Mica = DwmExtendFrameIntoClientArea(handle, ref margins) == 0;
            }
            var source = HwndSource.FromHwnd(handle);
            if (Mica) {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
            } else window.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
        }
    }
}
