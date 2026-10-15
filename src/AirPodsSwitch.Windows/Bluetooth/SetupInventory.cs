// SPDX-License-Identifier: GPL-3.0-only
using AirPodsSwitch.Windows.Media;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed record SetupInventory(IReadOnlyList<BluetoothRadio> Radios, IReadOnlyList<AdapterIdentifier> Adapters,
    IReadOnlyList<AudioEndpoint> Outputs, IReadOnlyList<string> DriverInterfaces)
{
    public static async Task<SetupInventory> ReadAsync(CancellationToken cancellationToken)
    {
        var radios = Task.Run(BluetoothInventory.Read, cancellationToken);
        var adapters = AdapterInventory.ReadAsync(cancellationToken);
        var audio = Task.Run(WindowsAudioManager.Read, cancellationToken);
        var driver = Task.Run(ProfileDriverTransport.InterfacePaths, cancellationToken);
        await Task.WhenAll(radios, adapters, audio, driver).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        return new(await radios, await adapters, (await audio).Endpoints, await driver);
    }

    public bool RadioOn(string? address) => Adapters.Any(a => a.Address == address && a.RadioState == "On");
    public int MatchingDrivers(string? address) => address is null ? 0 : DriverInterfaces.Count(p =>
        p.Contains("&" + address.Replace(":", "") + "_C", StringComparison.OrdinalIgnoreCase));
}
