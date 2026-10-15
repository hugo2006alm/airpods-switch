// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace AirPodsSwitch.Core.Protocol;

public readonly record struct BluetoothAddress(ulong Value)
{
    public static BluetoothAddress Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 6 || parts.Any(p => p.Length != 2 || !byte.TryParse(p,
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)))
            throw new FormatException("Expected Bluetooth address AA:BB:CC:DD:EE:FF");
        return new BluetoothAddress(Convert.ToUInt64(string.Concat(parts), 16));
    }
    public static BluetoothAddress FromBytes(ReadOnlySpan<byte> bytes, bool reversed)
    {
        if (bytes.Length != 6) throw new ArgumentException("A Bluetooth address requires six bytes");
        ulong value = 0;
        for (int i = 0; i < 6; i++) value = (value << 8) | bytes[reversed ? 5 - i : i];
        return new(value);
    }
    public byte[] Bytes(bool reversed = false)
    {
        byte[] result = new byte[6];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)(Value >> ((reversed ? i : 5 - i) * 8));
        return result;
    }
    public bool IsUsableHost => Value is > 0 and < 0xFFFFFFFFFFFF && Value != 0x020000000000;
    public override string ToString() => string.Join(':', Bytes().Select(b => b.ToString("X2")));
}
