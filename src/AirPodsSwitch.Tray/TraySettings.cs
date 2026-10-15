// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using AirPodsSwitch.Core.Diagnostics;

namespace AirPodsSwitch.Tray;

internal sealed record TraySettings(Guid HostId, string FriendlyHostName, string RadioAddress,
    string DeviceAddress, string EndpointId, string Transport, bool Notifications)
{
    public static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPodsSwitch");
    public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.private.json");
    public static TraySettings? Load()
    {
        if (!File.Exists(FilePath)) return null;
        if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException("Settings file too large");
        return JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(FilePath), JsonSettings.Options);
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonSettings.Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
