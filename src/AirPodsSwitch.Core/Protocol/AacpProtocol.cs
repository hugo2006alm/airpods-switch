// SPDX-License-Identifier: GPL-3.0-or-later
// Protocol layouts selectively adapted from LibrePods.
// Copyright (C) 2025 LibrePods contributors.
// Source files, commits and modifications: docs/UPSTREAM_MAP.md.
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace AirPodsSwitch.Core.Protocol;

public enum AudioSourceType { None = 0, Call = 1, Media = 2 }
public enum BudWear { Unknown, InEar, OutOfEar, InCase }
public sealed record AudioSource(BluetoothAddress Address, AudioSourceType Type);
public sealed record ConnectedDevice(BluetoothAddress Address, byte Info1, byte Info2);
public sealed record WearState(BudWear Left, BudWear Right)
{
    public bool IsWorn => Left == BudWear.InEar || Right == BudWear.InEar;
}
public sealed record BatteryState(byte Component, int? Percent, byte Status);
public sealed record DeviceIdentity(string ModelNumber, string Firmware)
{
    public AirPodsModel? Model => AirPodsModels.Find(ModelNumber);
}
public abstract record ProtocolEvent;
public sealed record HandshakeAcknowledged : ProtocolEvent;
public sealed record SourceReceived(AudioSource Source) : ProtocolEvent;
public sealed record DevicesReceived(ImmutableArray<ConnectedDevice> Devices) : ProtocolEvent;
public sealed record OwnershipReceived(bool Owns) : ProtocolEvent;
public sealed record WearReceived(WearState Wear) : ProtocolEvent;
public sealed record BatteryReceived(ImmutableArray<BatteryState> Batteries) : ProtocolEvent;
public sealed record IdentityReceived(DeviceIdentity Identity) : ProtocolEvent;
public sealed record RoutingReceived(BluetoothAddress Sender, bool Release, bool Reverse) : ProtocolEvent;
public sealed record OtherPacket(ushort Opcode, int Length) : ProtocolEvent;
public sealed record ProtocolStateInvalidated : ProtocolEvent;
public sealed record ParseResult(ProtocolEvent? Event, string? Error)
{
    public bool Success => Event is not null;
}

/// <summary>Parses one complete SDU. Never reassembles by searching arbitrary payload for a header.</summary>
public static class AacpProtocol
{
    public const int MaxPacketSize = 4096;
    public static byte[] Handshake() => Convert.FromHexString("00000400010002000000000000000000");
    public static byte[] Features() => Convert.FromHexString("040004004D00D700000000000000");
    public static byte[] Notifications() => Convert.FromHexString("040004000F00FFFFFFFF");
    public static byte[] SetOwnership(bool owns) => Frame(0x09, [0x06, (byte)(owns ? 1 : 0), 0, 0, 0]);

    public static byte[] Frame(ushort opcode, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPacketSize - 6) throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] packet = new byte[6 + payload.Length];
        new byte[] { 4, 0, 4, 0 }.CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), opcode);
        payload.CopyTo(packet.AsSpan(6));
        return packet;
    }

    /// <summary>Experimental minimum Hijackv2 request, directed to a host in the observed host list.</summary>
    public static byte[] Hijack(BluetoothAddress target)
    {
        if (!target.IsUsableHost) throw new ArgumentException("Invalid target host address");
        using var payload = new MemoryStream();
        payload.Write(target.Bytes(reversed: true));
        payload.Write(new byte[] { 0x62, 0x00, 0x01, 0xE5, 0x4A });
        payload.Write(Encoding.ASCII.GetBytes("localscore"));
        payload.Write(new byte[] { 0x30, 0x64, 0x46 });
        payload.Write(Encoding.ASCII.GetBytes("reason"));
        payload.WriteByte(0x48);
        payload.Write(Encoding.ASCII.GetBytes("Hijackv2"));
        payload.WriteByte(0x51);
        payload.Write(Encoding.ASCII.GetBytes("audioRoutingScore"));
        payload.Write(new byte[] { 0x31, 0x2D, 0x01, 0x5F });
        payload.Write(Encoding.ASCII.GetBytes("audioRoutingSetOwnershipToFalse"));
        payload.Write(new byte[] { 0x01, 0x4B });
        payload.Write(Encoding.ASCII.GetBytes("remotescore"));
        payload.WriteByte(0xA5);
        return Frame(0x10, payload.ToArray());
    }

    public static ParseResult Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length > MaxPacketSize) return Bad("Oversized SDU");
        if (packet.Length >= 4 && packet[..4].SequenceEqual(new byte[] { 1, 0, 4, 0 }))
            return new(new HandshakeAcknowledged(), null);
        if (packet.Length < 6) return Bad("Truncated AACP header");
        if (!packet[..4].SequenceEqual(new byte[] { 4, 0, 4, 0 })) return Bad("Invalid AACP header");
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet[4..]);
        switch (opcode)
        {
            case 0x0E:
                if (packet.Length != 13) return Bad("Audio source requires 13 bytes");
                if (packet[12] > 2) return Bad("Unknown audio source type");
                var sourceAddress = BluetoothAddress.FromBytes(packet.Slice(6, 6), true);
                if (packet[12] != 0 && !sourceAddress.IsUsableHost) return Bad("Active source has invalid host identity");
                return Ok(new SourceReceived(new(sourceAddress, (AudioSourceType)packet[12])));
            case 0x2E:
                if (packet.Length < 9) return Bad("Truncated connected-host count");
                int count = packet[8];
                if (count > 32 || packet.Length != 9 + count * 8) return Bad("Connected-host count/length mismatch");
                var devices = ImmutableArray.CreateBuilder<ConnectedDevice>(count);
                var addresses = new HashSet<BluetoothAddress>();
                for (int i = 0; i < count; i++)
                {
                    int offset = 9 + 8 * i;
                    var address = BluetoothAddress.FromBytes(packet.Slice(offset, 6), false);
                    if (!address.IsUsableHost || !addresses.Add(address)) return Bad("Invalid or duplicate host address");
                    devices.Add(new(address, packet[offset + 6], packet[offset + 7]));
                }
                return Ok(new DevicesReceived(devices.MoveToImmutable()));
            case 0x09:
                if (packet.Length != 11) return Bad("Control response requires four value bytes");
                if (packet[6] != 0x06) return Ok(new OtherPacket(opcode, packet.Length));
                if (packet[7] > 1) return Bad("Invalid ownership value");
                return Ok(new OwnershipReceived(packet[7] == 1));
            case 0x06:
                if (packet.Length != 8) return Bad("Ear state requires two bytes");
                return Ok(new WearReceived(new(Wear(packet[6]), Wear(packet[7]))));
            case 0x04:
                if (packet.Length < 7 || packet[6] > 4 || packet.Length != 7 + packet[6] * 5)
                    return Bad("Battery count/length mismatch");
                var batteries = ImmutableArray.CreateBuilder<BatteryState>(packet[6]);
                var components = new HashSet<byte>();
                for (int i = 0; i < packet[6]; i++)
                {
                    int offset = 7 + i * 5;
                    byte component = packet[offset], status = packet[offset + 3], percent = packet[offset + 2];
                    if (packet[offset + 1] != 1 || packet[offset + 4] != 1 ||
                        component is not (1 or 2 or 4 or 8) || !components.Add(component))
                        return Bad("Invalid battery component/spacers");
                    batteries.Add(new(component, status is 1 or 2 or 5 && percent <= 100 ? percent : null, status));
                }
                return Ok(new BatteryReceived(batteries.MoveToImmutable()));
            case 0x1D:
                return ParseIdentity(packet[6..]);
            case 0x11:
                if (packet.Length < 14) return Bad("Truncated routing relay");
                var sender = BluetoothAddress.FromBytes(packet.Slice(6, 6), true);
                // Routing payload is not fully specified; only conservative release/cancel hints
                // are exposed. A string match can never establish ownership or success.
                var routingText = Encoding.ASCII.GetString(packet[12..]);
                return Ok(new RoutingReceived(sender,
                    routingText.Contains("SetOwnershipToFalse", StringComparison.Ordinal),
                    routingText.Contains("ReverseBannerTapped", StringComparison.Ordinal)));
            default:
                // Includes key packets: ignore contents, never decode or log secrets.
                return Ok(new OtherPacket(opcode, packet.Length));
        }
    }

    private static ParseResult ParseIdentity(ReadOnlySpan<byte> payload)
    {
        // Upstream documentation and Android indexing differ around leading fields. Require
        // an exact model token immediately followed by the manufacturer, rather than shifting
        // strings silently. Retain no serial numbers, friendly names or updater identifiers.
        var fields = Encoding.UTF8.GetString(payload).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < fields.Length; i++)
        {
            string model = fields[i];
            if (model.Length == 5 && model[0] == 'A' && model.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 &&
                fields[i + 1] == "Apple Inc.")
                return Ok(new IdentityReceived(new(model, i + 3 < fields.Length ? fields[i + 3] : "unknown")));
        }
        return Bad("No unambiguous model/manufacturer pair in information packet");
    }
    private static BudWear Wear(byte value) => value switch
    {
        0 => BudWear.InEar, 1 => BudWear.OutOfEar, 2 => BudWear.InCase, _ => BudWear.Unknown
    };
    private static ParseResult Bad(string error) => new(null, error);
    private static ParseResult Ok(ProtocolEvent value) => new(value, null);
}
