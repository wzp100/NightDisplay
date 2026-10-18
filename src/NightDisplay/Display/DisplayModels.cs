using System;
using System.Runtime.InteropServices;

namespace NightDisplay
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Luid { public uint Low; public int High; public override string ToString() { return High + ":" + Low; } }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Region { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SignalInfo
    {
        public ulong PixelRate;
        public Rational HSync, VSync;
        public Region Active, Total;
        public uint VideoStandard, ScanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct ModeUnion
    {
        [FieldOffset(0)] public SignalInfo Target;
        [FieldOffset(0)] public SourceMode Source;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ModeInfo { public uint Type, Id; public Luid Adapter; public ModeUnion Data; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PathTarget
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetName
    {
        public DeviceHeader Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct AdapterName
    {
        public DeviceHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PreferredMode
    {
        public DeviceHeader Header;
        public uint Width, Height;
        public SignalInfo Signal;
    }

    public sealed class DisplaySnapshot
    {
        public PathInfo[] Paths;
        public ModeInfo[] Modes;
    }
    public sealed class DisplayTarget
    {
        public string Key, Name, DevicePath, AdapterPath;
        public bool Virtual, Active;
        public uint Technology;
        public PathInfo Path;
        public override string ToString() { return Name + (Virtual ? "  [虚拟屏]" : "  [实体屏]") + (Active ? " · 使用中" : ""); }
    }

}
