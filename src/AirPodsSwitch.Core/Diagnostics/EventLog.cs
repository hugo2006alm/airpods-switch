// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirPodsSwitch.Core.Diagnostics;

public static class JsonSettings
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Fixed-schema JSONL logger. Callers cannot accidentally attach arbitrary packet/key blobs.</summary>
public sealed class EventLog(TextWriter output, bool protocolDebug = false)
{
    private readonly object gate = new();
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);

    public string PrivateId(string value) => Convert.ToHexString(
        HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(value)))[..12];

    public void Write(string subsystem, string eventType, string result,
        string? previousState = null, string? newState = null, string? audioSourceType = null,
        int? connectedDeviceCount = null, string? transportState = null, Guid? requestId = null)
    {
        lock (gate)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                subsystem, eventType, previousState, newState, audioSourceType,
                connectedDeviceCount, transportState, takeoverRequestId = requestId, result
            }, JsonSettings.Options));
            output.Flush();
        }
    }

    public void Packet(ReadOnlySpan<byte> packet, string direction)
    {
        // Never emit raw payloads: opcodes 0x30/0x31 carry secret keys and information
        // packets/unknown extensions can contain private identifiers. Debug is metadata-only.
        if (!protocolDebug) return;
        var opcode = packet.Length >= 6 ? $"0x{packet[5]:X2}{packet[4]:X2}" : "short";
        Write("protocol", direction, $"opcode={opcode}; length={packet.Length}; payload=redacted");
    }
}
