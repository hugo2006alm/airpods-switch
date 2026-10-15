// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Channels;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;

namespace AirPodsSwitch.Tests;

public sealed class ClientTests
{
    private sealed class FakeTransport : IBluetoothTransport
    {
        public string Kind => "fake";
        public List<byte[]> Sent { get; } = [];
        public Channel<byte[]> Packets { get; } = Channel.CreateUnbounded<byte[]>();
        public bool Disposed;
        public Task OpenAsync(BluetoothAddress remote, BluetoothAddress local, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            Sent.Add(packet.ToArray());
            if (packet.Span.SequenceEqual(AacpProtocol.Handshake())) Packets.Writer.TryWrite([1, 0, 4, 0]);
            return Task.CompletedTask;
        }
        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken) => await Packets.Reader.ReadAsync(cancellationToken);
        public ValueTask DisposeAsync() { Disposed = true; Packets.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
    [Fact]
    public async Task HandshakeInitializationAndOwnershipSendDoNotInventObservedOwnership()
    {
        var transport = new FakeTransport();
        await using (var client = new AacpClient(transport, new EventLog(TextWriter.Null)))
        {
            await client.ConnectAsync(default, default, CancellationToken.None);
            Assert.Equal(3, transport.Sent.Count);
            await client.SendAsync(AacpProtocol.SetOwnership(true), CancellationToken.None);
            Assert.IsType<HandshakeAcknowledged>((await client.Incoming.ReadAsync()).Event);
            Assert.False(client.Incoming.TryRead(out _));
        }
        Assert.True(transport.Disposed);
    }
    [Fact]
    public async Task BadSourceInvalidatesStateInsteadOfKeepingRecentMedia()
    {
        var transport = new FakeTransport();
        await using var client = new AacpClient(transport, new EventLog(TextWriter.Null));
        await client.ConnectAsync(default, default, CancellationToken.None);
        _ = await client.Incoming.ReadAsync();
        transport.Packets.Writer.TryWrite(Convert.FromHexString("040004000E00665544332211FF"));
        var observed = await client.Incoming.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsType<ProtocolStateInvalidated>(observed.Event);
        var store = new AirPodsStateStore();
        long epoch = store.BeginConnection();
        store.Apply(new SourceReceived(new(BluetoothAddress.Parse("11:22:33:44:55:66"), AudioSourceType.Media)), 100, epoch);
        store.Apply(observed.Event, 101, epoch);
        Assert.Null(store.Current.Source);
    }
    [Fact]
    public void PacketLogsNeverIncludeSecretPayloadEvenWithDebugEnabled()
    {
        var text = new StringWriter();
        var packet = AacpProtocol.Frame(0x31, System.Text.Encoding.ASCII.GetBytes("SUPER-SECRET-KEY"));
        new EventLog(text).Packet(packet, "receive");
        Assert.Equal("", text.ToString());
        new EventLog(text, protocolDebug: true).Packet(packet, "receive");
        Assert.DoesNotContain("SUPER-SECRET", text.ToString());
        Assert.Contains("redacted", text.ToString());
    }
}
