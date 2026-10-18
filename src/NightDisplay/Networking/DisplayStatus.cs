using System;

namespace NightDisplay
{
    /// <summary>Published by the UI thread; HTTP readers never access the display API or disk.</summary>
    public sealed class DisplayStatus
    {
        public string Mode, Hotkey, RestoreHotkey;
        public string[] Displays;
        public bool VirtualAvailable;
        public long RestoreAfterUtcTicks;
        public object ToResponse()
        {
            return new {
                mode = Mode, displays = Displays, virtualAvailable = VirtualAvailable,
                hotkey = Hotkey, restoreHotkey = RestoreHotkey,
                countdown = RestoreAfterUtcTicks > 0 ? Math.Max(0, (int)TimeSpan.FromTicks(RestoreAfterUtcTicks - DateTime.UtcNow.Ticks).TotalSeconds) : 0
            };
        }
    }
}
