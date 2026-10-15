// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Principal;
using System.Text.Json;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Windows.Bluetooth;

return await DiagnosticProgram.RunAsync(args);

internal static class DiagnosticProgram
{
    public static Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("airpods-diag inventory | transport-probe\nAll output is diagnostic. No pairing, registry changes or takeover occur.");
                return Task.FromResult(0);
            }
            if (args[0] == "inventory")
            {
                using var identity = WindowsIdentity.GetCurrent();
                Print(new
                {
                    evidence = "VERIFIED LOCALLY: Windows cached inventory; AirPods routing not tested",
                    windows = Environment.OSVersion.VersionString,
                    administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
                    radios = BluetoothInventory.Read(),
                    aacp = "Requires a compatible installed Classic L2CAP profile driver; installed SDP service is not a live control link"
                });
                return Task.FromResult(0);
            }
            if (args[0] == "transport-probe")
            {
                Print(SocketProbe.Run());
                return Task.FromResult(0);
            }
            Console.Error.WriteLine("Unknown command. Use --help.");
            return Task.FromResult(2);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            new EventLog(Console.Error).Write("diagnostic", "error", ex.Message);
            return Task.FromResult(1);
        }
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonSettings.Options));
}
