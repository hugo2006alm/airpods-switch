// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using AirPodsSwitch.Core.Did;
using Microsoft.Win32;

namespace AirPodsSwitch.Windows.Did;

public sealed class WindowsDidRegistryStore : IDidRegistryStore
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public static string MachineFingerprint()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var id = key?.GetValue("MachineGuid") as string;
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Cannot bind backup to this machine");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }
    public RegistrySnapshot Read()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(DidManager.RegistryPath, writable: false);
        return new(key is not null, DidManager.Names.ToDictionary(n => n,
            n => key is null ? RegistryValue.Absent : ReadRaw(key, n)));
    }
    public void Write(string name, RegistryValue value)
    {
        if (!DidManager.Names.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("Non-DID value refused");
        if (!IsAdministrator()) throw new UnauthorizedAccessException("DID mutation requires administrator");
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(DidManager.RegistryPath, writable: true)
            ?? throw new InvalidOperationException("BTHPORT Parameters key missing");
        int error = value.Present
            ? RegSetValueEx(key.Handle.DangerousGetHandle(), name, 0, value.Kind, value.Data, (uint)value.Data.Length)
            : RegDeleteValue(key.Handle.DangerousGetHandle(), name);
        if (error != 0 && !(error == 2 && !value.Present)) throw new Win32Exception(error);
        key.Flush();
    }
    public IReadOnlyDictionary<string, RegistryValue> RelevantConfiguration()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(DidManager.RegistryPath);
        return new[] { "SupportedServices", "UnsupportedServices" }.ToDictionary(n => n,
            n => key is null ? RegistryValue.Absent : ReadRaw(key, n));
    }
    private static RegistryValue ReadRaw(RegistryKey key, string name)
    {
        uint length = 0;
        int error = RegQueryValueEx(key.Handle.DangerousGetHandle(), name, IntPtr.Zero, out uint kind, null, ref length);
        if (error == 2) return RegistryValue.Absent;
        if (error != 0 && error != 234) throw new Win32Exception(error);
        if (length > 65536) throw new InvalidOperationException("Registry value exceeds diagnostic size limit");
        byte[] data = new byte[length];
        error = RegQueryValueEx(key.Handle.DangerousGetHandle(), name, IntPtr.Zero, out kind, data, ref length);
        if (error != 0) throw new Win32Exception(error);
        Array.Resize(ref data, (int)length);
        return new(true, kind, data);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out uint type, [Out] byte[]? data, ref uint size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(IntPtr key, string name, uint reserved, uint type, byte[] data, uint size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteValueW")]
    private static extern int RegDeleteValue(IntPtr key, string name);
}
