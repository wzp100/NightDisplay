using System;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace NightDisplay
{
    // Native ABI v2: heartbeat, virtual flag, deadline, version; four aligned int64 values.
    public sealed class SafetyLease : IDisposable
    {
        readonly MemoryMappedFile memory;
        readonly MemoryMappedViewAccessor view;
        public readonly EventWaitHandle Changed, Ready;
        static string Name(string token) { return @"Local\NightDisplay.Lease." + token; }
        public SafetyLease(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("缺少恢复会话标识。", "token");
            memory = MemoryMappedFile.CreateOrOpen(Name(token), 32);
            view = memory.CreateViewAccessor();
            view.Write(24, 2L);
            Changed = new EventWaitHandle(false, EventResetMode.AutoReset, Name(token) + ".Changed");
            Ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name(token) + ".Ready");
        }
        public void Pulse() { view.Write(0, DateTime.UtcNow.Ticks); }
        public bool IsStale { get { return DateTime.UtcNow.Ticks - view.ReadInt64(0) > TimeSpan.FromSeconds(15).Ticks; } }
        public static void Publish(RecoveryState state)
        {
            if (string.IsNullOrEmpty(state.OwnerToken)) return;
            try {
                using (var memory = MemoryMappedFile.OpenExisting(Name(state.OwnerToken)))
                using (var view = memory.CreateViewAccessor()) {
                    view.Write(16, state.RestoreAfterUtcTicks);
                    Thread.MemoryBarrier(); view.Write(8, state.VirtualRequested ? 1L : 0L);
                }
                Notify(state.OwnerToken);
            } catch (System.IO.FileNotFoundException) { }
        }
        public static void Notify(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            try { using (var signal = EventWaitHandle.OpenExisting(Name(token) + ".Changed")) signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }
        public void Dispose() { Ready.Dispose(); Changed.Dispose(); view.Dispose(); memory.Dispose(); }
    }
}
