// SPDX-License-Identifier: GPL-3.0-only
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed record AdapterIdentifier(string DeviceInterfaceId, string Address, string RadioState);
public static class AdapterInventory
{
    public static async Task<IReadOnlyList<AdapterIdentifier>> ReadAsync(CancellationToken cancellationToken)
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector()).AsTask(cancellationToken);
        var result = new List<AdapterIdentifier>();
        foreach (var device in devices)
        {
            var adapter = await BluetoothAdapter.FromIdAsync(device.Id).AsTask(cancellationToken);
            if (adapter is null) continue;
            string state;
            try { state = (await adapter.GetRadioAsync().AsTask(cancellationToken))?.State.ToString() ?? "Unavailable"; }
            catch (UnauthorizedAccessException) { state = "Access denied to radio state"; }
            result.Add(new(device.Id, BluetoothInventory.FormatAddress(adapter.BluetoothAddress), state));
        }
        return result;
    }
}
