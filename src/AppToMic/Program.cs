namespace AppToMic;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Supported: --toggle, --on, --off, --show (handy for a Stream Deck "Open" action).
        string? command = args
            .Select(a => a.TrimStart('-', '/').ToLowerInvariant())
            .FirstOrDefault(a => a is "toggle" or "on" or "off" or "show");

        bool startMinimized = args.Any(a => a.TrimStart('-', '/').Equals("minimized", StringComparison.OrdinalIgnoreCase));
        var settings = Settings.Load();

        using var mutex = new Mutex(true, @"Local\AppToMic.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            RemoteControl.Send(settings.ControlPort, command ?? "show");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(settings, command, startMinimized));
        GC.KeepAlive(mutex);
    }
}
