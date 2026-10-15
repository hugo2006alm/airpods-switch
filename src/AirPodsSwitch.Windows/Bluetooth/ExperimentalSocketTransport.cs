// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using AirPodsSwitch.Core.Protocol;

namespace AirPodsSwitch.Windows.Bluetooth;

/// <summary>Opt-in feasibility probe. Provider creation is verified locally; PSM access and
/// receive/SDU behavior are not established. Never silently replace the driver transport.</summary>
public sealed class ExperimentalSocketTransport : IBluetoothTransport
{
    private Socket? socket;
    public string Kind => "winsock-experimental";
    public async Task OpenAsync(BluetoothAddress remote, BluetoothAddress local, CancellationToken cancellationToken)
    {
        if (socket is not null) throw new InvalidOperationException("Transport already open");
        socket = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)0x100);
        socket.Bind(new BluetoothEndpoint(local, uint.MaxValue));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var cancel = timeout.Token.Register(() => socket?.Dispose());
        // Blocking connect occurs on worker; closing socket cancels the kernel operation.
        await Task.Run(() => socket.Connect(new BluetoothEndpoint(remote, 0x1001)), timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
    }
    public async Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        var open = socket ?? throw new IOException("Transport closed");
        using var registration = cancellationToken.Register(open.Dispose);
        int sent = await Task.Run(() => open.Send(packet.Span, SocketFlags.None), cancellationToken);
        if (sent != packet.Length) throw new IOException("Partial experimental L2CAP write; no retry/reframing");
    }
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        var open = socket ?? throw new IOException("Transport closed");
        using var registration = cancellationToken.Register(open.Dispose);
        byte[] buffer = new byte[AacpProtocol.MaxPacketSize];
        int count = await Task.Run(() => open.Receive(buffer, SocketFlags.None), cancellationToken);
        return buffer[..count];
    }
    public ValueTask DisposeAsync() { socket?.Dispose(); socket = null; return ValueTask.CompletedTask; }

    private sealed class BluetoothEndpoint(BluetoothAddress address, uint port) : EndPoint
    {
        public override AddressFamily AddressFamily => (AddressFamily)32;
        public override SocketAddress Serialize()
        {
            // Windows SDK ws2bth.h uses pshpack1: ushort, ulong, GUID, uint (30 bytes).
            var result = new SocketAddress(AddressFamily, 30);
            for (int i = 0; i < 8; i++) result[2 + i] = (byte)(address.Value >> (8 * i));
            for (int i = 0; i < 4; i++) result[26 + i] = (byte)(port >> (8 * i));
            return result;
        }
        public override EndPoint Create(SocketAddress socketAddress)
        {
            if (socketAddress.Family != AddressFamily || socketAddress.Size < 30) throw new ArgumentException("Invalid Bluetooth endpoint");
            ulong value = 0;
            uint endpointPort = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)socketAddress[2 + i] << (8 * i);
            for (int i = 0; i < 4; i++) endpointPort |= (uint)socketAddress[26 + i] << (8 * i);
            return new BluetoothEndpoint(new(value), endpointPort);
        }
    }
}
