using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NightDisplay
{
    public static class GuardProcess
    {
        [StructLayout(LayoutKind.Sequential)] struct StartupInfo {
            public uint Size; public IntPtr Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
            public ushort Show, ReservedSize; public IntPtr Reserved2, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string exe, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static Process Start(string exe, string arguments)
        {
            IntPtr size = IntPtr.Zero; InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            IntPtr list = Marshal.AllocHGlobal(size), policy = Marshal.AllocHGlobal(8);
            bool initialized = false;
            try {
                if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                initialized = true;
                // This guard imports only Kernel32. Restrict DLL loading for this process alone;
                // it prevents unrelated desktop customization hooks from expanding its footprint.
                Marshal.WriteInt64(policy, 1L << 44);
                if (!UpdateProcThreadAttribute(list, 0, new IntPtr(0x20007), policy, new IntPtr(8), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup = new StartupInfoEx { Startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfoEx)) }, Attributes = list };
                ProcessInfo info;
                if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\" " + arguments), IntPtr.Zero, IntPtr.Zero, false,
                    0x00080000 | 0x08000000, IntPtr.Zero, null, ref startup, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { return Process.GetProcessById((int)info.Pid); }
                finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
            } finally { if (initialized) DeleteProcThreadAttributeList(list); Marshal.FreeHGlobal(policy); Marshal.FreeHGlobal(list); }
        }
    }
}
