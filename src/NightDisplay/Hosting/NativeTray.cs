using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NightDisplay
{
    /// <summary>Shell icon and on-demand native menu. No WinForms controls, fonts or bitmap cache.</summary>
    public sealed class NativeTray : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Data {
            public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Timeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref Data data);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        readonly IntPtr window;
        readonly Icon icon;
        readonly Action<int> action;
        Data data;
        readonly uint taskbarCreated = NativeDisplay.RegisterWindowMessage("TaskbarCreated");
        public NativeTray(IntPtr window, Action<int> action)
        {
            this.window = window; this.action = action;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NightDisplay.Icons.tray.ico"))
            using (var original = new Icon(stream)) icon = (Icon)original.Clone();
            data = new Data { Size = (uint)Marshal.SizeOf(typeof(Data)), Window = window, Id = 1,
                Flags = 1 | 2 | 4, Callback = HostWindow.TrayMessage, Icon = icon.Handle, Tip = "NightDisplay" };
            Add();
        }
        void Add() { Shell_NotifyIcon(0, ref data); data.Timeout = 4; Shell_NotifyIcon(4, ref data); }
        public void SetText(string text) { data.Tip = text; data.Flags = 4; Shell_NotifyIcon(1, ref data); }
        public void Receive(int message, IntPtr lParam)
        {
            if (message == taskbarCreated) { data.Flags = 1 | 2 | 4; Add(); }
            if (message != HostWindow.TrayMessage) return;
            int code = (int)(lParam.ToInt64() & 0xffff);
            if (code == 0x400 || code == 0x401 || code == 0x203) action(1);
            if (code == 0x7b) {
                IntPtr menu = CreatePopupMenu();
                try {
                    AppendMenu(menu, 0, new UIntPtr(1), "打开控制面板");
                    AppendMenu(menu, 0, new UIntPtr(2), "切换屏幕");
                    AppendMenu(menu, 0, new UIntPtr(3), "恢复实体屏");
                    AppendMenu(menu, 0x800, UIntPtr.Zero, null);
                    AppendMenu(menu, 0, new UIntPtr(4), "恢复并退出");
                    CursorPoint point; GetCursorPos(out point); SetForegroundWindow(window);
                    uint selected = TrackPopupMenuEx(menu, 0x100 | 0x80 | 2, point.X, point.Y, window, IntPtr.Zero);
                    if (selected != 0) action((int)selected);
                } finally { DestroyMenu(menu); NativeDisplay.PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero); }
            }
        }
        public void Dispose() { Shell_NotifyIcon(2, ref data); icon.Dispose(); }
    }
}
