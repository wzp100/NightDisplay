using System;
using System.Web.Script.Serialization;
using NightDisplay;

class Control
{
    static int Main(string[] args)
    {
        try {
            var request = new PanelRequest { Action = args.Length == 0 ? "status" : args[0] };
            if (args.Length > 1) request.Enabled = bool.Parse(args[1]);
            var state = PanelProtocol.Send(request).GetAwaiter().GetResult();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { state.HostPid, state.GuardPid, state.Mode,
                state.LanRunning, state.PanelRunning, Consent = state.Settings.LanConsentVersion,
                VirtualAvailable = state.Targets.Length > 0 }));
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
