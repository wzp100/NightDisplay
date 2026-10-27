using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace NightDisplay
{
    public sealed class Settings
    {
        public string VirtualDevicePath = "";
        public string VirtualDriverInstanceId = "";
        public uint Modifiers = 3;
        public uint ToggleKey = 0x78;
        public uint RestoreKey = 0x79;
        public bool KeepAwake = true;
        public bool LanEnabled = false;
        public int LanConsentVersion;
        public int LanPort = 47831;
        public string LanToken = "";
    }
    public sealed class RecoveryState
    {
        public bool VirtualRequested;
        public string VirtualDriverInstanceId = "";
        public string Paths = "", Modes = "";
        public string[] DevicePaths = new string[0];
        public int OwnerPid;
        public long OwnerStartTicks;
        public string OwnerToken = "";
        public long RestoreAfterUtcTicks;
    }
    public static class AppState
    {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightDisplay");
        public static string SettingsPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public static string RecoveryPath { get { return Path.Combine(DirectoryPath, "recovery.json"); } }
        public static string HeartbeatPath { get { return Path.Combine(DirectoryPath, "heartbeat.txt"); } }
        public static string LogPath { get { return Path.Combine(DirectoryPath, "nightdisplay.log"); } }
        public static readonly Encoding Utf8 = new UTF8Encoding(false);
        public static readonly object IoLock = new object();
        public static void WriteJson(string path, object value)
        {
            lock (IoLock)
            {
                Directory.CreateDirectory(DirectoryPath);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), Utf8);
                try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                var recovery = value as RecoveryState;
                if (recovery != null) SafetyLease.Publish(recovery);
            }
        }
        public static T ReadJson<T>(string path) where T : class, new()
        {
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Utf8))
                    return new JavaScriptSerializer().Deserialize<T>(reader.ReadToEnd()) ?? new T();
            }
            catch (Exception ex) { if (File.Exists(path)) Log("读取配置失败：" + ex.Message); return new T(); }
        }
        public static void Log(string message)
        {
            try {
                lock (IoLock) {
                    Directory.CreateDirectory(DirectoryPath);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2000000) {
                        for (int i = 2; i >= 1; i--) {
                            string next = LogPath + "." + (i + 1), previous = LogPath + "." + i;
                            if (File.Exists(next)) File.Delete(next);
                            if (File.Exists(previous)) File.Move(previous, next);
                        }
                        if (File.Exists(LogPath + ".1")) File.Delete(LogPath + ".1");
                        File.Move(LogPath, LogPath + ".1");
                    }
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, Utf8);
                }
            } catch { }
        }
        public static string Pack<T>(T[] items) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] data = new byte[size * items.Length];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try { for (int i = 0; i < items.Length; i++) { Marshal.StructureToPtr(items[i], ptr, false); Marshal.Copy(ptr, data, size * i, size); } }
            finally { Marshal.FreeHGlobal(ptr); }
            return Convert.ToBase64String(data);
        }
        public static T[] Unpack<T>(string value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] data = Convert.FromBase64String(value);
            if (data.Length % size != 0 || data.Length > 1024 * 1024) throw new InvalidDataException("恢复配置长度无效。");
            T[] items = new T[data.Length / size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try { for (int i = 0; i < items.Length; i++) { Marshal.Copy(data, i * size, ptr, size); items[i] = (T)Marshal.PtrToStructure(ptr, typeof(T)); } }
            finally { Marshal.FreeHGlobal(ptr); }
            return items;
        }
        public static void SetSnapshot(RecoveryState state, DisplaySnapshot snapshot)
        {
            state.Paths = Pack(snapshot.Paths);
            state.Modes = Pack(snapshot.Modes);
            state.DevicePaths = snapshot.Paths.Select(p => NativeDisplay.Describe(p).DevicePath).ToArray();
        }
        public static DisplaySnapshot GetSnapshot(RecoveryState state)
        {
            if (string.IsNullOrEmpty(state.Paths)) return null;
            try {
                var snapshot = new DisplaySnapshot { Paths = Unpack<PathInfo>(state.Paths), Modes = Unpack<ModeInfo>(state.Modes) };
                if (snapshot.Paths.Length != state.DevicePaths.Length) return null;
                for (int i = 0; i < snapshot.Paths.Length; i++)
                {
                    var target = NativeDisplay.Describe(snapshot.Paths[i]);
                    if (target.Virtual || !string.Equals(target.DevicePath, state.DevicePaths[i], StringComparison.OrdinalIgnoreCase)) return null;
                }
                return snapshot;
            } catch { return null; }
        }
        public static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("NightDisplay") != null;
        }
        public static void SetStartup(bool enabled, string exe)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("NightDisplay", "\"" + exe + "\" --startup", RegistryValueKind.String);
                else key.DeleteValue("NightDisplay", false);
            }
        }
    }
}
