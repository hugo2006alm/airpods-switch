// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed record SocketProbeResult(int Protocol, bool SocketCreated, int Error, string Meaning);

public static class SocketProbe
{
    public static IReadOnlyList<SocketProbeResult> Run()
    {
        var memory = Marshal.AllocHGlobal(512); // enough for WSADATA on supported x64/arm64 Windows
        try
        {
            int error = WSAStartup(0x202, memory);
            if (error != 0) throw new InvalidOperationException($"WSAStartup failed: {error}");
            try
            {
                return new[] { 3, 0x100 }.Select(protocol =>
                {
                    var handle = socket(32, 1, protocol); // AF_BTH, SOCK_STREAM; never connect
                    bool created = handle != new IntPtr(-1);
                    int lastError = created ? 0 : WSAGetLastError();
                    if (created) closesocket(handle);
                    return new SocketProbeResult(protocol, created, lastError,
                        protocol == 3 ? "RFCOMM socket creation only; not AACP" :
                        "L2CAP socket creation probe only; no host/channel/PSM validation");
                }).ToArray();
            }
            finally { WSACleanup(); }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [DllImport("ws2_32.dll")] private static extern int WSAStartup(ushort version, IntPtr data);
    [DllImport("ws2_32.dll")] private static extern int WSACleanup();
    [DllImport("ws2_32.dll")] private static extern IntPtr socket(int family, int type, int protocol);
    [DllImport("ws2_32.dll")] private static extern int closesocket(IntPtr socket);
    [DllImport("ws2_32.dll")] private static extern int WSAGetLastError();
}
