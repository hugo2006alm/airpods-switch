// SPDX-License-Identifier: GPL-3.0-only
namespace AirPodsSwitch.Core.Setup;

public enum ConnectionLoss { None, Suspended, RadioUnavailable, DeviceUnpaired, OutputUnavailable, InventoryUnavailable }

/// <summary>Recovery means invalidating the old session, never replaying an accepted takeover.</summary>
public static class ConnectionHealth
{
    public static ConnectionLoss Assess(bool suspended, bool inventoryAvailable, bool radioOn, bool paired, bool outputAvailable) =>
        suspended ? ConnectionLoss.Suspended : !inventoryAvailable ? ConnectionLoss.InventoryUnavailable :
        !radioOn ? ConnectionLoss.RadioUnavailable : !paired ? ConnectionLoss.DeviceUnpaired :
        !outputAvailable ? ConnectionLoss.OutputUnavailable : ConnectionLoss.None;

    public static string Instruction(ConnectionLoss reason) => reason switch
    {
        ConnectionLoss.Suspended => "Windows suspended the connection. After resume, refresh devices and reconnect.",
        ConnectionLoss.RadioUnavailable => "The Bluetooth radio is off or unavailable. Turn it on, refresh devices, and reconnect.",
        ConnectionLoss.DeviceUnpaired => "The selected AirPods are no longer paired. Pair them in Windows, refresh devices, and reconnect.",
        ConnectionLoss.OutputUnavailable => "The selected audio output disappeared. Reconnect the AirPods, refresh devices, and confirm the output.",
        ConnectionLoss.InventoryUnavailable => "Device information could not be refreshed. Refresh devices and reconnect.",
        _ => "Connection ended. Refresh devices and reconnect."
    };
}
