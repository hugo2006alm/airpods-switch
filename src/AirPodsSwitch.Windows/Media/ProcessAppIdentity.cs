// SPDX-License-Identifier: GPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AirPodsSwitch.Windows.Media;

internal static class ProcessAppIdentity
{
    // Read-only, limited-information handle. No process paths, titles, or command lines.
    public static string? Read(uint processId)
    {
        if (processId == 0) return null;
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return null;
        uint length = 0;
        if (GetApplicationUserModelId(handle, ref length, null) != 122 || length is < 2 or > 4096) return null;
        var value = new StringBuilder((int)length);
        return GetApplicationUserModelId(handle, ref length, value) == 0 ? value.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? value);
}
