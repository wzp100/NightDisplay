using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NightDisplay
{
    // Only the bundled MttVDD hardware ID is managed. Other virtual adapters are untouched.
    public sealed class VirtualDisplayDevice
    {
        public string InstanceId, Name;
        public bool Enabled;
        public string SelectionPath { get { return "nightdisplay:device:" + InstanceId; } }
        static readonly Guid DisplayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        [StructLayout(LayoutKind.Sequential)] struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct StateChange { public uint Size, Function, State, Scope, Profile; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder id, uint size, out uint required);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiSetClassInstallParams(IntPtr set, ref DeviceInfo info, ref StateChange change, uint size);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfo info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
        static void Require(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "控制虚拟显示设备失败"); }
        static string Property(IntPtr set, ref DeviceInfo info, uint property)
        {
            uint type, required;
            byte[] bytes = new byte[8192];
            return SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, bytes, (uint)bytes.Length, out required)
                ? Encoding.Unicode.GetString(bytes, 0, (int)required).TrimEnd('\0') : "";
        }
        static void Visit(Action<IntPtr, DeviceInfo, VirtualDisplayDevice> action)
        {
            Guid guid = DisplayClass;
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 2);
            Require(set != new IntPtr(-1));
            try {
                for (uint index = 0; ; index++) {
                    var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info)) { if (Marshal.GetLastWin32Error() != 259) Require(false); break; }
                    if (!Property(set, ref info, 1).Split('\0').Any(id => string.Equals(id, @"Root\MttVDD", StringComparison.OrdinalIgnoreCase))) continue;
                    var idText = new StringBuilder(512); uint required, status, problem;
                    Require(SetupDiGetDeviceInstanceId(set, ref info, idText, (uint)idText.Capacity, out required));
                    uint result = CM_Get_DevNode_Status(out status, out problem, info.DevInst, 0);
                    if (result != 0) throw new InvalidOperationException("无法读取虚拟显示设备状态：" + result);
                    action(set, info, new VirtualDisplayDevice { InstanceId = idText.ToString(), Name = Property(set, ref info, 0), Enabled = problem != 22 });
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
        }
        public static List<VirtualDisplayDevice> Installed()
        {
            var devices = new List<VirtualDisplayDevice>();
            Visit(delegate(IntPtr set, DeviceInfo info, VirtualDisplayDevice device) { devices.Add(device); });
            return devices;
        }
        public bool Matches(DisplayTarget target)
        {
            return string.Equals(target.DriverInstanceId, InstanceId, StringComparison.OrdinalIgnoreCase) ||
                (target.AdapterPath ?? "").StartsWith(@"\\?\" + InstanceId.Replace('\\', '#') + "#{", StringComparison.OrdinalIgnoreCase);
        }
        public DisplayTarget Placeholder()
        {
            return new DisplayTarget { Key = SelectionPath, DevicePath = SelectionPath, Name = Name + "（按需启用）", Virtual = true, DriverInstanceId = InstanceId };
        }
        public static void ChangeState(string instanceId, bool enabled)
        {
            bool found = false;
            Visit(delegate(IntPtr set, DeviceInfo info, VirtualDisplayDevice device) {
                if (!string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)) return;
                found = true;
                if (device.Enabled == enabled) return;
                var change = new StateChange { Size = 8, Function = 0x12, State = enabled ? 1u : 2u, Scope = 1 };
                Require(SetupDiSetClassInstallParams(set, ref info, ref change, (uint)Marshal.SizeOf(typeof(StateChange))));
                Require(SetupDiCallClassInstaller(0x12, set, ref info));
            });
            if (!found && enabled) throw new InvalidOperationException("虚拟显示设备已移除，请重新安装驱动。");
        }
        public static DisplayTarget Enable(DisplayTarget target, Action pulse)
        {
            if (string.IsNullOrEmpty(target.DriverInstanceId)) return target;
            DeviceControlTasks.SetEnabled(target.DriverInstanceId, true, pulse);
            for (int attempt = 0; attempt < 40; attempt++) {
                if (pulse != null) pulse();
                var device = Installed().FirstOrDefault(d => string.Equals(d.InstanceId, target.DriverInstanceId, StringComparison.OrdinalIgnoreCase));
                var current = device == null ? null : NativeDisplay.Targets().FirstOrDefault(t => device.Matches(t));
                if (current != null) { current.DriverInstanceId = target.DriverInstanceId; return current; }
                Thread.Sleep(150);
            }
            throw new InvalidOperationException("虚拟显示设备已启用，但 Windows 尚未创建显示路径，请稍后重试。");
        }
        public static void Disable(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return;
            var device = Installed().FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (device == null || !device.Enabled) return;
            // Never remove the remaining output before a physical monitor is verified active.
            var active = NativeDisplay.Query(false).Paths;
            if (!active.Any(p => !NativeDisplay.Describe(p).Virtual)) throw new InvalidOperationException("尚未恢复实体屏，已阻止关闭虚拟显示设备。");
            DeviceControlTasks.SetEnabled(instanceId, false, null);
            for (int attempt = 0; attempt < 40; attempt++) {
                if (!NativeDisplay.Targets().Any(t => device.Matches(t))) return;
                Thread.Sleep(150);
            }
            throw new InvalidOperationException("Windows 尚未移除虚拟显示路径，请稍后重试恢复。");
        }
    }
}
