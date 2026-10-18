using System;

namespace NightDisplay
{
    public static class Recovery
    {
        public static bool Restore(string expectedOwner = null)
        {
            var state = AppState.ReadJson<RecoveryState>(AppState.RecoveryPath);
            if (expectedOwner != null && state.OwnerToken != expectedOwner) return false;
            NativeDisplay.RestorePhysical(AppState.GetSnapshot(state));
            if (NativeDisplay.IsVirtualOnly()) throw new InvalidOperationException("实体屏恢复验证失败。");
            state.VirtualRequested = false; state.RestoreAfterUtcTicks = 0;
            AppState.WriteJson(AppState.RecoveryPath, state);
            AppState.Log("已恢复实体屏。");
            return true;
        }
    }
}
