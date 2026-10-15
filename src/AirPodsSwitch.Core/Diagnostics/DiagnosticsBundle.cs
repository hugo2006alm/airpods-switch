// SPDX-License-Identifier: GPL-3.0-only
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.Switching;

namespace AirPodsSwitch.Core.Diagnostics;

public sealed record DiagnosticsSummary(string AppVersion, string WindowsVersion, string Transport,
    bool RadioSelected, bool DeviceSelected, bool OutputSelected, bool Notifications,
    int? RadioCount, int? PairedDeviceCount, int? OutputCount, int? DriverInterfaceCount,
    bool InventoryAvailable, bool SessionRunning);

/// <summary>Export reconstructed, allowlisted metadata, never copies raw log or configuration files.</summary>
public static partial class DiagnosticsBundle
{
    public const int MaxLogBytes = 1024 * 1024;
    public static string[] FindRecentLogs(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "session-*.jsonl*")
            .Where(p => SessionFile().IsMatch(Path.GetFileName(p)))
            .OrderByDescending(File.GetLastWriteTimeUtc).Take(6).ToArray();
    }
    private static readonly HashSet<string> Subsystems = new(StringComparer.Ordinal)
        { "transport", "aacp", "protocol", "switch", "state", "tray", "notification", "recovery" };
    private static readonly HashSet<string> Events = new(StringComparer.Ordinal)
    {
        "open", "initialized", "closed", "takeover_write_failed", "commands_sent", "malformed_sdu",
        "send", "receive", "session_failed", "failed", "interrupted", "HandshakeAcknowledged", "SourceReceived",
        "DevicesReceived", "OwnershipReceived", "WearReceived", "BatteryReceived", "IdentityReceived",
        "RoutingReceived", "ProtocolStateInvalidated", "None", "Offer", "SendTakeover", "Cancel", "Confirmed", "Failed"
    };

    public static void Export(string destination, DiagnosticsSummary summary, IEnumerable<string> logFiles)
    {
        // CreateNew refuses to overwrite a backup, an existing export, or another user file.
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        Write(archive, "summary.json", JsonSerializer.Serialize(summary with
        {
            AppVersion = VersionToken(summary.AppVersion), WindowsVersion = VersionToken(summary.WindowsVersion),
            Transport = summary.Transport is "profile-driver" or "winsock-experimental" ? summary.Transport : "unknown"
        }, JsonSettings.Options));
        Write(archive, "README.txt", "Redacted support bundle. No addresses, host/device names, app IDs, titles, settings files, backups, raw packets, free-form log messages, or keys are included.\nOnly recent bounded log metadata is reconstructed. Truncated or invalid events are omitted.\n");
        int index = 0;
        foreach (string path in logFiles.Take(6))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                string tail = ReadTail(path);
                using var writer = new StreamWriter(archive.CreateEntry($"events/{++index}.jsonl").Open(), Encoding.UTF8);
                foreach (string line in tail.Split('\n').TakeLast(4000))
                    if (Sanitize(line) is { } sanitized) writer.WriteLine(sanitized);
            }
            catch (IOException) { /* A rotated/disappearing log must not break export. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string? Sanitize(string line)
    {
        if (line.Length is 0 or > 16384) return null;
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? subsystem = String(root, "subsystem"), eventType = String(root, "eventType");
            if (subsystem is null || !Subsystems.Contains(subsystem) || eventType is null || !Events.Contains(eventType)) return null;
            DateTimeOffset? at = root.TryGetProperty("timestamp", out var timestamp) &&
                timestamp.ValueKind == JsonValueKind.String && timestamp.TryGetDateTimeOffset(out var date) ? date : null;
            int? count = root.TryGetProperty("connectedDeviceCount", out var countValue) &&
                countValue.ValueKind == JsonValueKind.Number && countValue.TryGetInt32(out int hosts) && hosts is >= 0 and <= 32 ? hosts : null;
            string? transport = String(root, "transportState");
            if (transport is not ("CONNECTING" or "CONNECTED" or "CONNECTED_CONTROL_ONLY" or "DISCONNECTED")) transport = null;
            // Never include result: even an otherwise valid event can hold an exception path or packet.
            return JsonSerializer.Serialize(new
            {
                timestamp = at, subsystem, eventType,
                previousState = EnumToken<SwitchPhase>(String(root, "previousState")),
                newState = EnumToken<SwitchPhase>(String(root, "newState")),
                audioSourceType = EnumToken<AudioSourceType>(String(root, "audioSourceType")),
                connectedDeviceCount = count, transportState = transport
            }, JsonSettings.Options);
        }
        catch (JsonException) { return null; }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? EnumToken<T>(string? value) where T : struct, Enum =>
        value is not null && Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == value ? value : null;
    private static string VersionToken(string value) => SafeVersion().IsMatch(value) ? value : "unknown";
    [GeneratedRegex(@"\A[0-9][0-9A-Za-z.+-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeVersion();
    [GeneratedRegex(@"\Asession-[0-9-]+\.jsonl(?:\.[12])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SessionFile();
    private static string ReadTail(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, input.Length - MaxLogBytes);
        input.Seek(start, SeekOrigin.Begin);
        byte[] bytes = new byte[(int)Math.Min(input.Length - start, MaxLogBytes)];
        int read = 0, count;
        while (read < bytes.Length && (count = input.Read(bytes, read, bytes.Length - read)) != 0) read += count;
        string text = Encoding.UTF8.GetString(bytes, 0, read);
        // Seeking into a UTF-8/JSON line is allowed, but that partial first line is never parsed.
        return start == 0 ? text : text[(text.IndexOf('\n') + 1)..];
    }
    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
