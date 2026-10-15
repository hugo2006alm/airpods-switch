// SPDX-License-Identifier: GPL-3.0-only
using AirPodsSwitch.Core.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace AirPodsSwitch.Windows.Bluetooth;

/// <summary>Presence diagnostic only. Apple company/type is not paired-device/model identity proof.</summary>
public static class BleDiagnostic
{
    public static async Task WatchAsync(EventLog log, CancellationToken cancellationToken)
    {
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new HashSet<ulong>();
        watcher.Received += (_, args) =>
        {
            foreach (var manufacturer in args.Advertisement.ManufacturerData)
            {
                if (manufacturer.CompanyId != 0x004C || manufacturer.Data.Length is < 11 or > 64) continue;
                using var reader = DataReader.FromBuffer(manufacturer.Data);
                byte[] data = new byte[manufacturer.Data.Length];
                reader.ReadBytes(data);
                if (data[0] != 0x07) continue;
                lock (seen)
                {
                    if (seen.Count >= 256 || !seen.Add(args.BluetoothAddress)) continue;
                }
                log.Write("ble", "apple_proximity_seen",
                    $"advertiser={log.PrivateId(args.BluetoothAddress.ToString("X12"))}; modelField=0x{data[3]:X2}{data[4]:X2}; pairedAssociation=UNVERIFIED; wear/source=UNKNOWN");
            }
        };
        watcher.Stopped += (_, args) =>
        {
            if (args.Error != BluetoothError.Success) stopped.TrySetException(new IOException($"BLE watcher stopped: {args.Error}"));
            else stopped.TrySetResult();
        };
        watcher.Start();
        log.Write("ble", "started", "Passive manufacturer scan; no AirPods 5 identity inferred");
        try { await stopped.Task.WaitAsync(cancellationToken); }
        finally { watcher.Stop(); }
    }
}
