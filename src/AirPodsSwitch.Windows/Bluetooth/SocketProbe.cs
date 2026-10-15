// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using AirPodsSwitch.Core.Protocol;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed record SocketProbeResult(int RequestedProtocol, bool SocketCreated, int Error,
    int? ActualProtocol, string? Provider, int? LocalBindError, string Meaning);

public static class SocketProbe
{
    public static IReadOnlyList<SocketProbeResult> Run(BluetoothAddress? localRadio = null)
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
                    int? actualProtocol = null;
                    string? provider = null;
                    int? bindError = null;
                    if (created)
                    {
                        // WSAPROTOCOL_INFOW: iProtocol at byte 92, szProtocol at 116.
                        // Some providers accept protocol 0x100 but return RFCOMM (3).
                        var details = Marshal.AllocHGlobal(628);
                        try
                        {
                            int size = 628;
                            if (getsockopt(handle, 0xFFFF, 0x2005, details, ref size) == 0)
                            {
                                actualProtocol = Marshal.ReadInt32(details, 92);
                                provider = Marshal.PtrToStringUni(details + 116);
                            }
                            if (localRadio is { } radio && protocol == 0x100)
                            {
                                byte[] endpoint = new byte[30];
                                endpoint[0] = 32;
                                for (int i = 0; i < 8; i++) endpoint[2 + i] = (byte)(radio.Value >> (8 * i));
                                for (int i = 26; i < 30; i++) endpoint[i] = 0xFF; // BT_PORT_ANY
                                bindError = bind(handle, endpoint, endpoint.Length) == 0 ? 0 : WSAGetLastError();
                            }
                        }
                        finally { Marshal.FreeHGlobal(details); closesocket(handle); }
                    }
                    return new SocketProbeResult(protocol, created, lastError, actualProtocol, provider, bindError,
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
    [DllImport("ws2_32.dll")] private static extern int getsockopt(IntPtr socket, int level, int option, IntPtr value, ref int size);
    [DllImport("ws2_32.dll")] private static extern int bind(IntPtr socket, byte[] address, int length);
    [DllImport("ws2_32.dll")] private static extern int WSAGetLastError();
}
