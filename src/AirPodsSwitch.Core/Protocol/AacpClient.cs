// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Channels;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.State;

namespace AirPodsSwitch.Core.Protocol;

public sealed record IncomingPacket(ProtocolEvent Event, long At);

/// <summary>One reader, serialized writes, bounded mailbox. A send never updates observed state.</summary>
public sealed class AacpClient(IBluetoothTransport transport, EventLog log) : IAsyncDisposable
{
    private readonly Channel<IncomingPacket> incoming = Channel.CreateBounded<IncomingPacket>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    private readonly TaskCompletionSource handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? reader;
    public ChannelReader<IncomingPacket> Incoming => incoming.Reader;

    public async Task ConnectAsync(BluetoothAddress remote, BluetoothAddress local, CancellationToken cancellationToken)
    {
        await transport.OpenAsync(remote, local, cancellationToken);
        reader = ReadAsync(lifetime.Token);
        await SendAsync(AacpProtocol.Handshake(), cancellationToken);
        await handshake.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await SendAsync(AacpProtocol.Features(), cancellationToken);
        await SendAsync(AacpProtocol.Notifications(), cancellationToken);
    }
    public async Task SendAsync(byte[] packet, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await writer.WaitAsync(timeout.Token);
        try
        {
            log.Packet(packet, "send");
            await transport.SendAsync(packet, timeout.Token);
        }
        finally { writer.Release(); }
    }
    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] packet = await transport.ReceiveAsync(cancellationToken);
                long at = MonotonicClock.Milliseconds;
                if (packet.Length == 0) throw new IOException("AACP transport closed");
                log.Packet(packet, "receive");
                var parsed = AacpProtocol.Parse(packet);
                if (!parsed.Success)
                {
                    log.Write("aacp", "malformed_sdu", parsed.Error!);
                    await incoming.Writer.WriteAsync(new(new ProtocolStateInvalidated(), at), cancellationToken)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }
                if (parsed.Event is HandshakeAcknowledged) handshake.TrySetResult();
                if (parsed.Event is not OtherPacket)
                    await incoming.Writer.WriteAsync(new(parsed.Event!, at), cancellationToken)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            handshake.TrySetException(error);
            incoming.Writer.TryComplete(error);
            return;
        }
        finally { incoming.Writer.TryComplete(); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await transport.DisposeAsync();
        if (reader is not null)
        {
            try { await reader.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { }
        }
        writer.Dispose();
        lifetime.Dispose();
    }
}
