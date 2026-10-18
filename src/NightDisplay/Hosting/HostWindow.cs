using System;
using System.Collections.Concurrent;
using System.Windows.Forms;

namespace NightDisplay
{
    /// <summary>Invisible top-level HWND receives broadcasts and hotkeys, without a Form/control tree.</summary>
    public sealed class HostWindow : NativeWindow, IDisposable
    {
        public const int DispatchMessage = 0x8001, TrayMessage = 0x8002;
        readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        readonly Action<int, IntPtr, IntPtr> receive;
        public HostWindow(Action<int, IntPtr, IntPtr> receive)
        {
            this.receive = receive;
            CreateHandle(new CreateParams { Caption = "NightDisplay.Background", Style = 0 });
        }
        public void Post(Action action)
        {
            pending.Enqueue(action);
            if (!NativeDisplay.PostMessage(Handle, DispatchMessage, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("后台消息窗口不可用。");
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == DispatchMessage) { Action action; while (pending.TryDequeue(out action)) action(); }
            else receive(message.Msg, message.WParam, message.LParam);
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }
}
