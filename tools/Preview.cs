using System;
using NightDisplay;
class Preview
{
    [STAThread] static void Main(string[] args) { PanelWindow.RenderPreview(args[0], args.Length > 1 && args[1] == "--dark"); }
}
