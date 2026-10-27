using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Xml;

namespace NightDisplay
{
    // Fixed, on-demand actions avoid a UAC dialog while the physical displays are off.
    // Users may execute these tasks, but only administrators may change their definitions.
    public static class DeviceControlTasks
    {
        const string Folder = "\\NightDisplay";
        const string Permissions = "D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;BU)";
        public static bool SupportsTasks { get { return Path.GetFullPath(Program.Exe).StartsWith(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); } }
        static object Call(object instance, string method, params object[] args) { return instance.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, instance, args); }
        static object Get(object instance, string property) { return instance.GetType().InvokeMember(property, BindingFlags.GetProperty, null, instance, null); }
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        static object Connect()
        {
            object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            try { Call(service, "Connect"); return service; } catch { Release(service); throw; }
        }
        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        static string TaskName(string instanceId, bool enabled)
        {
            // A device instance ID is encoded, never interpreted as a task path or argument.
            return (enabled ? "Enable-" : "Disable-") + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(instanceId.ToUpperInvariant())).Replace('/', '_').Replace('+', '-').TrimEnd('=');
        }
        static string Arguments(string instanceId, bool enabled) { return (enabled ? "--device-enable " : "--device-disable ") + "\"" + instanceId + "\""; }
        static bool MatchesTask(object task, string instanceId, bool enabled)
        {
            var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml((string)Get(task, "Xml"));
            var namespaces = new XmlNamespaceManager(xml.NameTable); namespaces.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            var command = xml.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", namespaces);
            var arguments = xml.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Arguments", namespaces);
            return command != null && arguments != null && string.Equals(command.InnerText, Program.Exe, StringComparison.OrdinalIgnoreCase) && arguments.InnerText == Arguments(instanceId, enabled);
        }
        static bool TrustedOwner(string sid)
        {
            return sid == "S-1-5-18" || sid == "S-1-5-32-544" || sid == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        }
        static void RequireProtectedPath(string path)
        {
            const FileSystemRights writable = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | (FileSystemRights)0x50000000;
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)); directory != null; directory = directory.Parent) {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("设备控制程序不能位于链接目录中。");
                // Ancestors outside Program Files may allow users to create siblings; the installation itself must remain protected.
                if (!directory.FullName.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)) break;
                var security = directory.GetAccessControl();
                if (!TrustedOwner(security.GetOwner(typeof(SecurityIdentifier)).Value)) throw new InvalidOperationException("安装目录不属于系统或管理员，请重新安装。");
                foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier))) {
                    string sid = rule.IdentityReference.Value;
                    if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 || (rule.FileSystemRights & writable) == 0) continue;
                    if (!TrustedOwner(sid))
                        throw new InvalidOperationException("安装目录允许普通账户修改，请重新安装到受保护的 Program Files 目录。");
                }
            }
            var fileSecurity = File.GetAccessControl(path);
            if (!TrustedOwner(fileSecurity.GetOwner(typeof(SecurityIdentifier)).Value)) throw new InvalidOperationException("设备控制程序不属于系统或管理员，请重新安装。");
            foreach (FileSystemAccessRule rule in fileSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier))) {
                string sid = rule.IdentityReference.Value;
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & writable) != 0 && !TrustedOwner(sid))
                    throw new InvalidOperationException("设备控制程序允许普通账户修改，请重新安装。");
            }
        }
        public static void Configure()
        {
            if (!IsAdministrator()) throw new InvalidOperationException("配置虚拟显示设备控制需要管理员权限。");
            // A SYSTEM task must never execute a portable EXE in a user-writable directory.
            string executable = Path.GetFullPath(Program.Exe);
            if (!SupportsTasks) throw new InvalidOperationException("请使用安装版，将 NightDisplay 安装到 Program Files 后启用按需虚拟屏。");
            RequireProtectedPath(executable);
            object service = null, folder = null, rootFolder = null;
            try {
                service = Connect(); rootFolder = Call(service, "GetFolder", "\\");
                try { folder = Call(service, "GetFolder", Folder); }
                catch (TargetInvocationException) { folder = Call(rootFolder, "CreateFolder", "NightDisplay", Permissions); }
                foreach (var device in VirtualDisplayDevice.Installed()) foreach (bool enabled in new[] { false, true }) {
                    string xml = "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Principals><Principal id=\"System\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                        "<Settings><MultipleInstancesPolicy>Queue</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT30S</ExecutionTimeLimit></Settings>" +
                        "<Actions Context=\"System\"><Exec><Command>" + SecurityElement.Escape(executable) + "</Command><Arguments>" + SecurityElement.Escape(Arguments(device.InstanceId, enabled)) +
                        "</Arguments><WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(executable)) + "</WorkingDirectory></Exec></Actions></Task>";
                    object task = Call(folder, "RegisterTask", TaskName(device.InstanceId, enabled), xml, 6, "SYSTEM", null, 5, Permissions);
                    Release(task);
                }
            } finally { Release(folder); Release(rootFolder); Release(service); }
        }
        public static void Remove()
        {
            object service = null, folder = null, tasks = null;
            try {
                service = Connect(); folder = Call(service, "GetFolder", Folder); tasks = Call(folder, "GetTasks", 1);
                for (int i = Convert.ToInt32(Get(tasks, "Count")); i > 0; i--) {
                    object task = tasks.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, tasks, new object[] { i });
                    try { Call(folder, "DeleteTask", Get(task, "Name"), 0); } finally { Release(task); }
                }
                Release(folder); folder = Call(service, "GetFolder", "\\"); Call(folder, "DeleteFolder", "NightDisplay", 0);
            } catch (TargetInvocationException ex) { AppState.Log("移除设备控制任务：" + ex.InnerException.Message); }
            finally { Release(tasks); Release(folder); Release(service); }
        }
        public static void EnsureAvailable(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || IsAdministrator()) return;
            if (HasTask(instanceId, true) && HasTask(instanceId, false)) return;
            if (!SupportsTasks) throw new InvalidOperationException("按需启停虚拟屏需要安装到 Program Files，或以管理员身份运行便携版。");
            using (var process = Process.Start(new ProcessStartInfo(Program.Exe, "--configure-device-control") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) {
                process.WaitForExit();
                if (process.ExitCode != 0 || !HasTask(instanceId, true) || !HasTask(instanceId, false)) throw new InvalidOperationException("虚拟屏按需启停尚未配置，请使用 Program Files 下的安装版并完成管理员授权。");
            }
        }
        static bool HasTask(string instanceId, bool enabled)
        {
            object service = null, task = null, folder = null;
            try { service = Connect(); folder = Call(service, "GetFolder", Folder); task = Call(folder, "GetTask", TaskName(instanceId, enabled)); return MatchesTask(task, instanceId, enabled); }
            catch (TargetInvocationException) { return false; }
            finally { Release(task); Release(folder); Release(service); }
        }
        static bool Finished(object running)
        {
            try { Call(running, "Refresh"); int state = Convert.ToInt32(Get(running, "State")); return state != 4 && state != 2; }
            catch (TargetInvocationException ex) {
                // Short-lived tasks can finish before the first refresh. Their instance then disappears.
                if (ex.InnerException != null && ex.InnerException.HResult == unchecked((int)0x8004130B)) return true;
                throw;
            }
        }
        public static void SetEnabled(string instanceId, bool enabled, Action pulse)
        {
            if (IsAdministrator()) { VirtualDisplayDevice.ChangeState(instanceId, enabled); return; }
            object service = null, folder = null, task = null, running = null;
            try {
                service = Connect(); folder = Call(service, "GetFolder", Folder); task = Call(folder, "GetTask", TaskName(instanceId, enabled));
                if (!MatchesTask(task, instanceId, enabled)) throw new InvalidOperationException("设备控制任务与当前安装路径不一致，请重新配置。");
                running = Call(task, "Run", (object)null);
                var elapsed = Stopwatch.StartNew();
                do {
                    if (pulse != null) pulse();
                    Thread.Sleep(100);
                    if (Finished(running)) {
                        // LastTaskResult belongs to the most recent instance, which may be another queued request.
                        var device = VirtualDisplayDevice.Installed().FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
                        if ((device == null && enabled) || (device != null && device.Enabled != enabled)) throw new InvalidOperationException("虚拟显示设备启停失败，请查看设备管理器。");
                        return;
                    }
                } while (elapsed.ElapsedMilliseconds < 10000);
                throw new TimeoutException("虚拟显示设备启停超时，请稍后重试。");
            } catch (TargetInvocationException ex) { throw new InvalidOperationException("无法控制虚拟显示设备，请重新安装或配置 NightDisplay。", ex.InnerException); }
            finally { Release(running); Release(task); Release(folder); Release(service); }
        }
    }
}
