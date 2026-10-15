// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AirPodsSwitch.Windows.Bluetooth;

public sealed record BluetoothRadio(string Address, string Name, uint ClassOfDevice,
    ushort Manufacturer, ushort LmpSubversion, IReadOnlyList<PairedDevice> Devices);
public sealed record PairedDevice(string Address, string Name, bool Connected, bool Remembered,
    bool Authenticated, uint ClassOfDevice, bool NameSuggestsAirPods, IReadOnlyList<Guid> Services);

public static class BluetoothInventory
{
    public static readonly Guid AacpService = new("74ec2172-0bad-4d01-8f77-997b2be0722a");

    public static string FormatAddress(ulong address) => string.Join(":",
        Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (8 * i)) & 255).ToString("X2")));

    public static IReadOnlyList<BluetoothRadio> Read()
    {
        var result = new List<BluetoothRadio>();
        var parameters = new FindRadioParameters { Size = (uint)Marshal.SizeOf<FindRadioParameters>() };
        var find = Native.BluetoothFindFirstRadio(ref parameters, out var radio);
        if (find == IntPtr.Zero)
        {
            radio?.Dispose();
            var error = Marshal.GetLastWin32Error();
            if (error is 259 or 1168) return result; // no radio, not a successful connection
            throw new Win32Exception(error, "Bluetooth radio enumeration failed");
        }
        try
        {
            while (true)
            {
                using (radio)
                {
                    var info = new RadioInfo { Size = (uint)Marshal.SizeOf<RadioInfo>() };
                    var error = Native.BluetoothGetRadioInfo(radio, ref info);
                    if (error != 0) throw new Win32Exception((int)error);
                    result.Add(new BluetoothRadio(FormatAddress(info.Address), info.Name,
                        info.ClassOfDevice, info.Manufacturer, info.LmpSubversion, Devices(radio)));
                }
                if (Native.BluetoothFindNextRadio(find, out radio)) continue;
                var lastError = Marshal.GetLastWin32Error();
                radio?.Dispose();
                if (lastError != 259) throw new Win32Exception(lastError);
                break;
            }
        }
        finally { Native.BluetoothFindRadioClose(find); }
        return result;
    }

    private static IReadOnlyList<PairedDevice> Devices(SafeFileHandle radio)
    {
        var result = new List<PairedDevice>();
        var search = new FindDeviceParameters
        {
            Size = (uint)Marshal.SizeOf<FindDeviceParameters>(), ReturnAuthenticated = true,
            ReturnRemembered = true, ReturnConnected = true, Radio = radio.DangerousGetHandle()
            // Cached inventory only: never initiate inquiry, pair or connect.
        };
        var device = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
        var find = Native.BluetoothFindFirstDevice(ref search, ref device);
        if (find == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 259 or 1168) return result;
            throw new Win32Exception(error, "Bluetooth device enumeration failed");
        }
        try
        {
            do
            {
                uint count = 0;
                var serviceResult = Native.BluetoothEnumerateInstalledServices(radio, ref device, ref count, null);
                Guid[] services = [];
                if (serviceResult == 234 && count is > 0 and <= 128)
                {
                    services = new Guid[count];
                    serviceResult = Native.BluetoothEnumerateInstalledServices(radio, ref device, ref count, services);
                }
                if (serviceResult != 0 && serviceResult != 234)
                    throw new Win32Exception((int)serviceResult, "Installed service enumeration failed");
                result.Add(new PairedDevice(FormatAddress(device.Address), device.Name, device.Connected,
                    device.Remembered, device.Authenticated, device.ClassOfDevice,
                    device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase), services));
                device.Size = (uint)Marshal.SizeOf<DeviceInfo>();
            } while (Native.BluetoothFindNextDevice(find, ref device));
            var error = Marshal.GetLastWin32Error();
            if (error != 259) throw new Win32Exception(error);
        }
        finally { Native.BluetoothFindDeviceClose(find); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParameters { public uint Size; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RadioInfo
    {
        public uint Size;
        public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
        public uint ClassOfDevice;
        public ushort LmpSubversion;
        public ushort Manufacturer;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FindDeviceParameters
    {
        public uint Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }
    private static class Native
    {
        [DllImport("BluetoothApis.dll", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParameters parameters, out SafeFileHandle radio);
        [DllImport("BluetoothApis.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextRadio(IntPtr find, out SafeFileHandle radio);
        [DllImport("BluetoothApis.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindRadioClose(IntPtr find);
        [DllImport("BluetoothApis.dll")]
        public static extern uint BluetoothGetRadioInfo(SafeFileHandle radio, ref RadioInfo info);
        [DllImport("BluetoothApis.dll", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref FindDeviceParameters parameters, ref DeviceInfo device);
        [DllImport("BluetoothApis.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceInfo device);
        [DllImport("BluetoothApis.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindDeviceClose(IntPtr find);
        [DllImport("BluetoothApis.dll")]
        public static extern uint BluetoothEnumerateInstalledServices(SafeFileHandle radio, ref DeviceInfo device,
            ref uint serviceCount, [Out] Guid[]? services);
    }
}
