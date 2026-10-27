using System;
using System.Linq;
using System.Threading;

namespace NightDisplay
{
    public static class Recovery
    {
        sealed class Operation : IDisposable
        {
            readonly Mutex mutex = new Mutex(false, @"Local\NightDisplay.DisplayOperation.v1");
            public Operation()
            {
                try { if (!mutex.WaitOne(30000)) { mutex.Dispose(); throw new TimeoutException("显示器操作仍在进行，请稍后重试。"); } }
                catch (AbandonedMutexException) { }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
        public static IDisposable LockOperation() { return new Operation(); }
        public static void RestoreOutput(Func<bool> physicalActive, Action restorePhysical, Action disableVirtual)
        {
            if (!physicalActive()) restorePhysical();
            if (!physicalActive()) throw new InvalidOperationException("实体屏恢复验证失败。");
            disableVirtual();
        }
        public static bool Restore(string expectedOwner = null)
        {
            using (LockOperation()) return RestoreCore(expectedOwner);
        }
        static bool RestoreCore(string expectedOwner)
        {
            var state = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath);
            if (expectedOwner != null && state.OwnerToken != expectedOwner) return false;
            // Preserve display changes made with Win+P after virtual mode has already ended.
            RestoreOutput(delegate { return NativeDisplay.Query(false).Paths.Any(p => !NativeDisplay.Describe(p).Virtual); },
                delegate { NativeDisplay.RestorePhysical(AppState.GetSnapshot(state)); }, delegate { VirtualDisplayDevice.Disable(state.VirtualDriverInstanceId); });
            state.VirtualRequested = false; state.RestoreAfterUtcTicks = 0;
            state.VirtualDriverInstanceId = "";
            AppState.WriteJson(AppState.RecoveryPath, state);
            AppState.Log("已恢复实体屏。");
            return true;
        }
    }
}
