using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NightDisplay
{
    public static class DriverInstaller
    {
        [StructLayout(LayoutKind.Sequential)] struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid guid, IntPtr parent);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid guid, string desc, IntPtr parent, uint flags, ref DeviceInfo data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint property, byte[] buffer, uint length);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfo data);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent, string hardwareId, string inf, uint flags, out bool reboot);
        static void Require(bool success, string action) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), action); }
        public static bool Installed()
        {
            using (var search = new System.Management.ManagementObjectSearcher("SELECT HardwareID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'ROOT%'"))
            using (var result = search.Get())
                foreach (System.Management.ManagementObject device in result) {
                    using (device) {
                        var ids = device["HardwareID"] as string[];
                        if (ids != null && ids.Any(id => string.Equals(id, @"Root\MttVDD", StringComparison.OrdinalIgnoreCase))) return true;
                    }
                }
            return false;
        }
        public static void VerifyPackage(string folder)
        {
            string manifest = Path.Combine(folder, "sha256.txt");
            if (!File.Exists(manifest)) throw new FileNotFoundException("缺少驱动校验清单。", manifest);
            foreach (string line in File.ReadAllLines(manifest, AppState.Utf8))
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) throw new InvalidDataException("驱动校验清单格式错误。");
                if (parts[0].Length != 64 || parts[1] != Path.GetFileName(parts[1])) throw new InvalidDataException("驱动校验清单路径无效。");
                string path = Path.Combine(folder, parts[1]);
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                    if (!string.Equals(actual, parts[0], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("驱动文件校验失败：" + parts[1]);
                }
            }
        }
        public static void Install(string folder)
        {
            VerifyPackage(folder);
            string configFolder = @"C:\VirtualDisplayDriver";
            Directory.CreateDirectory(configFolder);
            string config = Path.Combine(configFolder, "vdd_settings.xml");
            // Preserve any existing configuration from the user's other VDD installation.
            if (!File.Exists(config)) File.Copy(Path.Combine(folder, "vdd_settings.xml"), config);
            bool created = false;
            IntPtr set = new IntPtr(-1);
            var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
            try
            {
                if (!Installed())
                {
                    Guid guid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
                    set = SetupDiCreateDeviceInfoList(ref guid, IntPtr.Zero);
                    Require(set != new IntPtr(-1), "创建设备列表失败");
                    Require(SetupDiCreateDeviceInfo(set, "Display", ref guid, "NightDisplay Virtual Display", IntPtr.Zero, 1, ref info), "创建虚拟显示设备失败");
                    byte[] id = Encoding.Unicode.GetBytes("Root\\MttVDD\0\0");
                    Require(SetupDiSetDeviceRegistryProperty(set, ref info, 1, id, (uint)id.Length), "设置设备标识失败");
                    Require(SetupDiCallClassInstaller(0x19, set, ref info), "注册虚拟显示设备失败");
                    created = true;
                }
                bool reboot;
                Require(UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, @"Root\MttVDD", Path.GetFullPath(Path.Combine(folder, "MttVDD.inf")), 1, out reboot), "安装签名驱动失败");
                AppState.Log("虚拟显示驱动安装成功，需重启=" + reboot);
                File.WriteAllText(Path.Combine(AppState.DirectoryPath, "driver-install-result.txt"), reboot ? "安装完成，Windows 提示需要重启。" : "安装完成。", AppState.Utf8);
            }
            catch
            {
                if (created) SetupDiCallClassInstaller(5, set, ref info);
                throw;
            }
            finally { if (set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set); }
        }
        public static void ConfigureFirewall()
        {
            string name = "NightDisplay LAN Control";
            using (var remove = Process.Start(new ProcessStartInfo("netsh.exe", "advfirewall firewall delete rule name=\"" + name + "\"") { UseShellExecute = false, CreateNoWindow = true })) remove.WaitForExit();
            string args = "advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow program=\"" + Program.Exe + "\" protocol=TCP localport=47831 remoteip=LocalSubnet profile=any enable=yes";
            using (var add = Process.Start(new ProcessStartInfo("netsh.exe", args) { UseShellExecute = false, CreateNoWindow = true }))
            {
                add.WaitForExit();
                if (add.ExitCode != 0) throw new InvalidOperationException("添加局域网防火墙规则失败。");
            }
            AppState.Log("已添加仅限 LocalSubnet 的应用防火墙规则。");
        }
    }
}
