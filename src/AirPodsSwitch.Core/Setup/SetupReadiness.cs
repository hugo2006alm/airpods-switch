// SPDX-License-Identifier: GPL-3.0-only
namespace AirPodsSwitch.Core.Setup;

public sealed record SetupInputs(bool RadioSelected, bool RadioOn, bool DevicePaired,
    bool EndpointAvailable, bool EndpointConfirmed, string Transport, int MatchingDriverInterfaces,
    int RadioCount, bool InventoryAvailable = true);
public sealed record SetupReadiness(bool CanConnect, string Instruction)
{
    public static SetupReadiness Assess(SetupInputs input)
    {
        if (!input.InventoryAvailable) return new(false, "Device information is unavailable. Refresh devices to try again.");
        if (!input.RadioSelected) return new(false, "1. Select a Bluetooth radio. If none appear, check Windows Bluetooth settings.");
        if (!input.RadioOn) return new(false, "1. Turn on the selected Bluetooth radio in Windows, then refresh devices.");
        if (!input.DevicePaired) return new(false, "2. Pair and connect your AirPods in Windows Bluetooth settings, then refresh devices.");
        if (!input.EndpointAvailable) return new(false, "3. Connect the AirPods audio output in Windows Sound settings, then refresh devices.");
        if (!input.EndpointConfirmed) return new(false, "3. Select the matching AirPods output and confirm it below.");
        if (input.Transport == "profile-driver")
        {
            if (input.RadioCount != 1) return new(false, "4. The installed control driver requires one Bluetooth radio for unambiguous binding.");
            if (input.MatchingDriverInterfaces != 1) return new(false,
                "4. No unique compatible control-driver connection is available for these AirPods. Check their connection and driver support in the setup guide. No driver is installed by this app.");
        }
        else if (input.Transport != "winsock-experimental") return new(false, "4. Select a supported control connection.");
        return new(true, input.Transport == "winsock-experimental"
            ? "Ready to try the experimental direct connection. Windows may reject this transport."
            : "Ready to connect. A control connection alone does not confirm audio switching.");
    }
}
