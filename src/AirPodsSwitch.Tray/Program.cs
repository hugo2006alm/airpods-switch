// SPDX-License-Identifier: GPL-3.0-only
namespace AirPodsSwitch.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test")
        {
            try
            {
                var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddText("Runtime validation; not displayed").BuildNotification();
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new
                {
                    result = "Native notification object constructed; no registration or notification displayed",
                    notification.Id
                }));
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { result = "failed", error = error.ToString() }));
                return 1;
            }
        }
        using var instance = new Mutex(true, @"Local\AirPodsSwitch.Tray", out bool first);
        if (!first) return 0;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayForm());
        return 0;
    }
}
