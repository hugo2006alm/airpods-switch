// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using AirPodsSwitch.Core.Diagnostics;

namespace AirPodsSwitch.Core.Did;

/// <summary>Raw registry bytes preserve all kinds, unexpanded strings, and original absence.</summary>
public sealed record RegistryValue(bool Present, uint Kind, byte[] Data)
{
    public static RegistryValue Absent => new(false, 0, []);
    public static RegistryValue Dword(uint value) => new(true, 4, BitConverter.GetBytes(value));
    public bool Same(RegistryValue other) => Present == other.Present && Kind == other.Kind && Data.SequenceEqual(other.Data);
}
public sealed record RegistrySnapshot(bool KeyExists, Dictionary<string, RegistryValue> Values)
{
    public bool Same(RegistrySnapshot other) => KeyExists == other.KeyExists && Values.Count == other.Values.Count &&
        Values.All(p => other.Values.TryGetValue(p.Key, out var value) && p.Value.Same(value));
}
public interface IDidRegistryStore
{
    RegistrySnapshot Read();
    void Write(string name, RegistryValue value);
}
public sealed record DidBackupData(int Schema, string MachineFingerprint, string RegistryPath,
    string View, DateTimeOffset CreatedAt, RegistrySnapshot Original);
public sealed record DidBackup(DidBackupData Backup, string Sha256);

public sealed class DidManager(IDidRegistryStore store, string machineFingerprint, Func<bool> isAdministrator)
{
    public const string RegistryPath = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters";
    public static readonly string[] Names = ["DIDVendorIDSource", "DIDVendorID", "DIDProductID", "DIDVersion"];
    public RegistrySnapshot Status() => store.Read();

    public DidBackup Capture()
    {
        var data = new DidBackupData(1, machineFingerprint, RegistryPath, "Registry64", DateTimeOffset.UtcNow, store.Read());
        var result = new DidBackup(data, Hash(data));
        Validate(result);
        return result;
    }

    public void Validate(DidBackup backup)
    {
        if (backup.Backup is not { } data || data.Schema != 1 || data.MachineFingerprint != machineFingerprint ||
            data.RegistryPath != RegistryPath || data.View != "Registry64" || data.Original?.Values is not { } values ||
            values.Count != Names.Length || Names.Any(n => !values.ContainsKey(n)))
            throw new InvalidOperationException("Backup schema, machine or registry scope mismatch");
        foreach (var value in values.Values)
        {
            if (value is null || value.Data is null || value.Data.Length > 65536 || value.Kind > 11 ||
                !value.Present && (value.Kind != 0 || value.Data.Length != 0) ||
                value.Present && value.Kind is 4 or 5 && value.Data.Length != 4 ||
                value.Present && value.Kind == 11 && value.Data.Length != 8 ||
                !data.Original.KeyExists && value.Present)
                throw new InvalidOperationException("Invalid raw registry value in backup");
        }
        if (!string.Equals(backup.Sha256, Hash(data), StringComparison.Ordinal))
            throw new InvalidOperationException("Backup checksum mismatch; preserve original backup");
    }

    public RegistrySnapshot PlannedAppleState(DidBackup backup)
    {
        Validate(backup);
        var values = backup.Backup.Original.Values.ToDictionary(p => p.Key, p => p.Value);
        values[Names[0]] = RegistryValue.Dword(1);
        values[Names[1]] = RegistryValue.Dword(0x4C);
        return new(true, values);
    }

    public void ApplyApple(DidBackup backup, string confirmation)
    {
        RequireMutation(backup, confirmation, "APPLY-APPLE");
        var original = backup.Backup.Original;
        if (!original.KeyExists) throw new InvalidOperationException("BTHPORT Parameters key absent; refusing to create a system key");
        var applied = PlannedAppleState(backup);
        var current = store.Read();
        if (current.Same(applied)) return;
        if (!current.Same(original)) throw new InvalidOperationException("Registry changed since backup; refusing overwrite");
        Change(current, applied);
    }

    public void Restore(DidBackup backup, string confirmation)
    {
        RequireMutation(backup, confirmation, "RESTORE-DID");
        var current = store.Read();
        if (current.Same(backup.Backup.Original)) return;
        if (!current.Same(PlannedAppleState(backup)))
            throw new InvalidOperationException("Unexpected registry edits; restore requires manual review using original backup");
        Change(current, backup.Backup.Original);
    }

    private void RequireMutation(DidBackup backup, string actual, string expected)
    {
        Validate(backup);
        if (actual != expected) throw new InvalidOperationException($"Explicit --confirm {expected} is required");
        if (!isAdministrator()) throw new UnauthorizedAccessException("Run the diagnostic command as administrator for this explicit change");
    }
    private void Change(RegistrySnapshot from, RegistrySnapshot to)
    {
        var expected = new RegistrySnapshot(from.KeyExists, from.Values.ToDictionary(p => p.Key, p => p.Value));
        var written = new List<string>();
        try
        {
            foreach (var name in Names.Where(name => !from.Values[name].Same(to.Values[name])))
            {
                if (!store.Read().Same(expected)) throw new InvalidOperationException("Concurrent DID edit detected");
                // Add before writing: a backend may mutate and then report an error.
                written.Add(name);
                store.Write(name, to.Values[name]);
                expected.Values[name] = to.Values[name];
            }
            if (!store.Read().Same(to)) throw new InvalidOperationException("DID readback differs from intended configuration");
        }
        catch (Exception originalError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (string name in written.AsEnumerable().Reverse())
            {
                try
                {
                    // Do not overwrite a third party's concurrent value during rollback.
                    var actual = store.Read().Values[name];
                    if (actual.Same(to.Values[name])) store.Write(name, from.Values[name]);
                    else if (!actual.Same(from.Values[name]))
                        throw new InvalidOperationException($"Concurrent edit prevented rollback of {name}");
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("DID change and rollback failed; retain backup and inspect registry manually",
                    new[] { originalError }.Concat(rollbackErrors));
            throw;
        }
    }
    private static string Hash(DidBackupData data) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(data, JsonSettings.Options)));

    public static void SaveNew(string path, DidBackup backup)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, backup, JsonSettings.Options);
        file.Flush(flushToDisk: true);
    }
    public static DidBackup Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 1024 * 1024) throw new InvalidOperationException("Backup file exceeds size limit");
        return JsonSerializer.Deserialize<DidBackup>(file, JsonSettings.Options) ?? throw new InvalidOperationException("Empty backup");
    }
}
