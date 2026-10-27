using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace NightDisplay
{
    public static class NativeDisplay
    {
        public const uint Apply = 0x80, Validate = 0x40, Supplied = 0x20, SaveDatabase = 0x200, AllowChanges = 0x400;
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);
        [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint paths, [In] PathInfo[] pathArray, uint modes, [In] ModeInfo[] modeArray, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetAdapterName(ref AdapterName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetPreferredMode(ref PreferredMode mode);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] public static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        public static DisplaySnapshot Query(bool all)
        {
            return QueryCore(all ? 1u : 2u, IntPtr.Zero);
        }
        public static DisplaySnapshot QueryDatabase()
        {
            IntPtr topology = Marshal.AllocHGlobal(4);
            try { return QueryCore(4, topology); } finally { Marshal.FreeHGlobal(topology); }
        }
        static DisplaySnapshot QueryCore(uint flags, IntPtr topology)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint pc, mc;
                Check(GetDisplayConfigBufferSizes(flags, out pc, out mc), "枚举显示器");
                var paths = new PathInfo[Math.Max(pc, 1)];
                var modes = new ModeInfo[Math.Max(mc, 1)];
                int result = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, topology);
                if (result == 122) continue;
                Check(result, "读取显示器配置");
                Array.Resize(ref paths, (int)pc);
                Array.Resize(ref modes, (int)mc);
                return new DisplaySnapshot { Paths = paths, Modes = modes };
            }
            throw new InvalidOperationException("显示器配置持续变化，请稍后重试。");
        }
        public static string Key(PathInfo p) { return p.Target.Adapter + ":" + p.Target.Id; }
        public static DisplayTarget Describe(PathInfo p)
        {
            var name = new TargetName { Header = new DeviceHeader { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = p.Target.Adapter, Id = p.Target.Id } };
            var adapter = new AdapterName { Header = new DeviceHeader { Type = 4, Size = (uint)Marshal.SizeOf(typeof(AdapterName)), Adapter = p.Target.Adapter } };
            Check(GetTargetName(ref name), "读取屏幕名称");
            GetAdapterName(ref adapter);
            string description = (name.Friendly + " " + name.DevicePath + " " + adapter.DevicePath).ToLowerInvariant();
            bool isVirtual = p.Target.Technology == 16 || p.Target.Technology == 17 || description.Contains("virtual") || description.Contains("vdd") || description.Contains("idd") || description.Contains("gameviewer");
            return new DisplayTarget {
                Key = Key(p), Name = string.IsNullOrWhiteSpace(name.Friendly) ? "显示器 " + p.Target.Id : name.Friendly,
                DevicePath = name.DevicePath, AdapterPath = adapter.DevicePath, Virtual = isVirtual,
                Active = (p.Flags & 1) != 0, Technology = p.Target.Technology, Path = p
            };
        }
        public static List<DisplayTarget> Targets()
        {
            var result = new Dictionary<string, DisplayTarget>();
            foreach (var path in Query(true).Paths)
            {
                if (!path.Target.Available) continue;
                string key = Key(path);
                if (result.ContainsKey(key)) continue;
                try { result[key] = Describe(path); } catch (Win32Exception) { }
            }
            return result.Values.OrderBy(x => x.Virtual).ThenBy(x => x.Name).ToList();
        }
        public static bool IsVirtualOnly()
        {
            var paths = Query(false).Paths;
            return paths.Length > 0 && paths.All(p => Describe(p).Virtual);
        }
        public static void Check(int error, string operation)
        {
            if (error != 0) throw new Win32Exception(error, operation + "失败（" + error + "）：" + new Win32Exception(error).Message);
        }
        public static void RestoreSnapshot(DisplaySnapshot snapshot, bool persist)
        {
            if (snapshot == null || snapshot.Paths == null || snapshot.Paths.Length == 0) throw new InvalidOperationException("没有恢复配置。");
            uint baseFlags = Supplied | AllowChanges;
            Check(SetDisplayConfig((uint)snapshot.Paths.Length, snapshot.Paths, (uint)snapshot.Modes.Length, snapshot.Modes, baseFlags | Validate), "验证恢复配置");
            Check(SetDisplayConfig((uint)snapshot.Paths.Length, snapshot.Paths, (uint)snapshot.Modes.Length, snapshot.Modes, baseFlags | Apply | (persist ? SaveDatabase : 0)), "恢复显示配置");
        }
        public static DisplaySnapshot PhysicalSnapshot()
        {
            var current = Query(false);
            current.Paths = current.Paths.Where(p => !Describe(p).Virtual).ToArray();
            if (current.Paths.Length == 0) throw new InvalidOperationException("当前没有启用的实体屏，请先切回实体屏。");
            return current;
        }
        public static DisplaySnapshot SingleTarget(DisplayTarget target, bool explicit1080)
        {
            var p = target.Path;
            p.Flags = 1;
            p.Source.ModeIndex = uint.MaxValue;
            p.Target.ModeIndex = uint.MaxValue;
            p.Target.Rotation = 1;
            p.Target.Scaling = 1;
            p.Target.Refresh = new Rational { Numerator = 0, Denominator = 0 };
            p.Target.ScanLineOrdering = 0;
            uint width = 1920, height = 1080;
            if (!explicit1080)
            {
                var preferred = new PreferredMode { Header = new DeviceHeader { Type = 3, Size = (uint)Marshal.SizeOf(typeof(PreferredMode)), Adapter = p.Target.Adapter, Id = p.Target.Id } };
                Check(GetPreferredMode(ref preferred), "读取实体屏首选分辨率");
                width = preferred.Width; height = preferred.Height;
            }
            p.Source.ModeIndex = 0;
            // A refresh rate must not be supplied when the target mode is omitted.
            var modes = new[] { new ModeInfo { Type = 1, Id = p.Source.Id, Adapter = p.Source.Adapter, Data = new ModeUnion { Source = new SourceMode { Width = width, Height = height, PixelFormat = 4, Position = new Point() } } } };
            return new DisplaySnapshot { Paths = new[] { p }, Modes = modes };
        }
        public static void ValidateVirtual(DisplayTarget target)
        {
            var desired = SingleTarget(target, true);
            Check(SetDisplayConfig(1, desired.Paths, (uint)desired.Modes.Length, desired.Modes, Supplied | AllowChanges | Validate), "验证虚拟屏");
        }
        public static void ActivateVirtual(DisplayTarget target)
        {
            if (!target.Virtual) throw new InvalidOperationException("选中的显示器不是虚拟屏。");
            RestoreSnapshot(SingleTarget(target, true), false);
        }
        public static void RestorePhysical(DisplaySnapshot preferred)
        {
            if (preferred != null)
            {
                try {
                    RestoreSnapshot(preferred, true);
                    if (Query(false).Paths.Any(p => !Describe(p).Virtual)) return;
                } catch (Exception) { }
            }
            foreach (var target in Targets().Where(t => !t.Virtual).OrderByDescending(t => t.Active).ThenByDescending(t => t.Technology == 0x80000000))
            {
                try {
                    RestoreSnapshot(SingleTarget(target, false), true);
                    if (Query(false).Paths.Any(p => !Describe(p).Virtual)) return;
                } catch (Exception) { }
            }
            throw new InvalidOperationException("未能恢复实体屏。请使用 Win + P 切换，或连接实体显示器。");
        }
    }
}
