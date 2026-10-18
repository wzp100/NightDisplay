using System;

namespace NightDisplay
{
    /// <summary>Owns the recoverable transition, independently of desktop and HTTP presentation.</summary>
    public static class DisplaySwitchService
    {
        public static void EnterVirtual(DisplayTarget target, RecoveryState recovery, int duration)
        {
            if (target == null || !target.Virtual) throw new ArgumentException("需要可用虚拟显示器。", "target");
            if (duration < 0) throw new ArgumentOutOfRangeException("duration");
            var baseline = NativeDisplay.PhysicalSnapshot();
            NativeDisplay.ValidateVirtual(target);
            // Save physical-only topology before requesting temporary virtual-only output.
            NativeDisplay.RestoreSnapshot(baseline, true);
            AppState.SetSnapshot(recovery, NativeDisplay.PhysicalSnapshot());
            recovery.VirtualRequested = true;
            recovery.RestoreAfterUtcTicks = duration > 0 ? DateTime.UtcNow.AddSeconds(duration).Ticks : 0;
            AppState.WriteJson(AppState.RecoveryPath, recovery);
            try {
                NativeDisplay.ActivateVirtual(target);
                if (!NativeDisplay.IsVirtualOnly()) throw new InvalidOperationException("切换后未检测到仅虚拟屏输出。");
                AppState.Log("虚拟输出启用，自动恢复秒数=" + duration);
            }
            catch { try { Recovery.Restore(); } catch { } throw; }
        }
    }
}
