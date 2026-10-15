// SPDX-License-Identifier: GPL-3.0-or-later
namespace AirPodsSwitch.Core.Protocol;

public interface IBluetoothTransport : IAsyncDisposable
{
    string Kind { get; }
    Task OpenAsync(BluetoothAddress remote, BluetoothAddress local, CancellationToken cancellationToken);
    Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}
