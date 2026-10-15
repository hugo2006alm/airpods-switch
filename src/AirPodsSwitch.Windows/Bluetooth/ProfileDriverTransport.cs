// SPDX-License-Identifier: GPL-3.0-or-later
// Device interface and SDU transport adapted from librepods-windowsbridge contributors.
// Exact source: docs/UPSTREAM_MAP.md. Original implementation rewritten with managed
// asynchronous I/O and explicit bounded lifetime; no driver binaries or installers included.
using System.ComponentModel;
using System.Runtime.InteropServices;
using AirPodsSwitch.Core.Protocol;
using Microsoft.Win32.SafeHandles;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed class ProfileDriverTransport : IBluetoothTransport
{
    public static readonly Guid InterfaceGuid = new("9eec98bb-3c54-45d4-a843-7900c4635e08");
    private FileStream? stream;
    public string Kind => "profile-driver";

    public static IReadOnlyList<string> InterfacePaths()
    {
        var guid = InterfaceGuid;
        IntPtr list = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (list == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var paths = new List<string>();
        try
        {
            var data = new DeviceInterface { Size = (uint)Marshal.SizeOf<DeviceInterface>() };
            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref guid, i, ref data))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(list, ref data, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
                if (needed is < 8 or > 65536) throw new InvalidOperationException("Invalid driver interface size");
                IntPtr buffer = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref data, buffer, needed, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    paths.Add(Marshal.PtrToStringUni(buffer + 4) ?? throw new IOException("Empty driver path"));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        return paths;
    }
    public async Task OpenAsync(BluetoothAddress remote, BluetoothAddress local, CancellationToken cancellationToken)
    {
        if (stream is not null) throw new InvalidOperationException("Transport already open");
        // Select by explicit paired Classic address. Never take the first unknown interface.
        string address = remote.ToString().Replace(":", "");
        var paths = InterfacePaths().Where(p => p.Contains("&" + address + "_C", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length != 1) throw new IOException("No unique AAP profile-driver interface for selected AirPods; driver missing, device disconnected, or ambiguous binding");
        var opening = Task.Run(() =>
        {
            var handle = CreateFile(paths[0], 0xC0000000, 0, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Cannot open AAP profile driver; check device binding and exclusive use");
            }
            try { return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: true); }
            catch { handle.Dispose(); throw; }
        }, CancellationToken.None);
        try { stream = await opening.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken); }
        catch
        {
            // CreateFile itself is synchronous. Dispose any late successful open instead of leaking
            // it or resurrecting a cancelled transport. No retries or detached UI callbacks.
            _ = opening.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
    public async Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        if (packet.Length > AacpProtocol.MaxPacketSize) throw new ArgumentOutOfRangeException(nameof(packet));
        await (stream ?? throw new IOException("Transport closed")).WriteAsync(packet, cancellationToken);
    }
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[AacpProtocol.MaxPacketSize];
        int count = await (stream ?? throw new IOException("Transport closed")).ReadAsync(buffer, cancellationToken);
        return buffer[..count];
    }
    public async ValueTask DisposeAsync()
    {
        if (stream is { } open) { stream = null; await open.DisposeAsync(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterface { public uint Size; public Guid ClassGuid; public uint Flags; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr list, IntPtr device, ref Guid guid, uint index, ref DeviceInterface data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr list, ref DeviceInterface data, IntPtr details,
        uint size, out uint required, IntPtr deviceInfo);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint mode, uint flags, IntPtr template);
}
