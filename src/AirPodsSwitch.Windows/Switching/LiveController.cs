// SPDX-License-Identifier: GPL-3.0-or-later
using System.Threading.Channels;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;
using AirPodsSwitch.Windows.Bluetooth;
using AirPodsSwitch.Windows.Media;

namespace AirPodsSwitch.Windows.Switching;

public sealed record LiveOptions(BluetoothAddress Device, BluetoothAddress Radio, string? EndpointId,
    string Transport = "profile-driver", bool OfferSwitch = false);
public sealed record UserDecision(Guid RequestId, bool Accept);

/// <summary>One serialized owner of protocol state and policy. UI posts request IDs only.</summary>
public sealed class LiveController(LiveOptions options, EventLog log)
{
    private readonly AirPodsStateStore store = new();
    private readonly SwitchCoordinator coordinator = new();
    private readonly Channel<UserDecision> actions = Channel.CreateBounded<UserDecision>(32);
    private readonly WindowsMediaMonitor media = new();
    public AirPodsState State => store.Current;
    public SwitchDecision? LastDecision { get; private set; }
    public event Action<AirPodsState>? StateChanged;
    public event Action<SwitchDecision>? Decision;
    public bool Post(UserDecision decision) => actions.Writer.TryWrite(decision);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var inventory = BluetoothInventory.Read();
        var radio = inventory.SingleOrDefault(r => r.Address == options.Radio.ToString())
            ?? throw new InvalidOperationException("Selected radio is not in current inventory");
        if (!radio.Devices.Any(d => d.Address == options.Device.ToString() && (d.Authenticated || d.Remembered)))
            throw new InvalidOperationException("Selected AirPods are not paired on selected radio; pair explicitly in Windows first");
        if (options.Transport == "profile-driver" && inventory.Count != 1)
            throw new InvalidOperationException("Profile-driver radio binding ambiguous; use one active radio for this experiment");
        if (options.OfferSwitch && string.IsNullOrEmpty(options.EndpointId))
            throw new InvalidOperationException("Takeover requires explicitly mapped AirPods endpoint ID from audio inventory");
        IBluetoothTransport transport = options.Transport switch
        {
            "profile-driver" => new ProfileDriverTransport(),
            "winsock-experimental" => new ExperimentalSocketTransport(),
            _ => throw new ArgumentException("Unknown transport")
        };
        await using var client = new AacpClient(transport, log);
        try
        {
            log.Write("transport", "open", transport.Kind, transportState: "CONNECTING");
            await client.ConnectAsync(options.Device, options.Radio, cancellationToken);
            long epoch = store.BeginConnection();
            log.Write("transport", "initialized", "Handshake acknowledged; await model/source evidence", transportState: "CONNECTED_CONTROL_ONLY");
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            SwitchInputs? inputs = null;
            long lastSampleAt = 0;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Drain(client, epoch);
                if (client.Incoming.Completion.IsCompleted)
                {
                    await client.Incoming.Completion;
                    throw new IOException("AACP receive channel ended");
                }
                long now = MonotonicClock.Milliseconds;
                if (options.OfferSwitch && (inputs is null || now - lastSampleAt >= 500))
                {
                    var sample = await media.SampleAsync(options.EndpointId, cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                    if (!sample.Audio.Endpoints.Any(e => e.Id == options.EndpointId))
                        throw new InvalidOperationException("Mapped AirPods endpoint unavailable; select exact active endpoint in Windows audio settings");
                    inputs = new(store.Current, options.Radio, sample.State, sample.Render);
                    lastSampleAt = MonotonicClock.Milliseconds;
                }
                if (!options.OfferSwitch) continue;
                Drain(client, epoch);
                now = MonotonicClock.Milliseconds;
                inputs = inputs! with { AirPods = store.Current };
                while (actions.Reader.TryRead(out var action))
                {
                    var decision = action.Accept ? coordinator.Accept(action.RequestId, inputs, now)
                        : coordinator.Reject(action.RequestId, now);
                    Emit(decision);
                    if (decision.Action != SwitchAction.SendTakeover) continue;
                    try
                    {
                        var source = store.Current.Source!.Value;
                        var target = source.Type == AudioSourceType.Media ? source.Address :
                            store.Current.Devices!.Value.Single(d => d.Address != options.Radio).Address;
                        await client.SendAsync(AacpProtocol.SetOwnership(true), cancellationToken);
                        Drain(client, epoch);
                        var guard = coordinator.Update(inputs with { AirPods = store.Current }, MonotonicClock.Milliseconds);
                        if (guard.Action == SwitchAction.Failed) { Emit(guard); continue; }
                        await client.SendAsync(AacpProtocol.Hijack(target), cancellationToken);
                        log.Write("switch", "commands_sent", "Experimental minimum sequence; no success implied", requestId: decision.RequestId);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        log.Write("transport", "takeover_write_failed", error.GetType().Name, requestId: decision.RequestId);
                        Emit(coordinator.SendFailed(MonotonicClock.Milliseconds));
                    }
                }
                Drain(client, epoch);
                Emit(coordinator.Update(inputs with { AirPods = store.Current }, MonotonicClock.Milliseconds));
            }
        }
        finally
        {
            store.Disconnect();
            StateChanged?.Invoke(store.Current);
            log.Write("transport", "closed", "No automatic reconnect", transportState: "DISCONNECTED");
        }
    }
    private void Drain(AacpClient client, long epoch)
    {
        while (client.Incoming.TryRead(out var incoming))
        {
            if (!store.Apply(incoming.Event, incoming.At, epoch)) continue;
            log.Write("state", incoming.Event.GetType().Name, "Observation applied",
                audioSourceType: State.Source?.Value.Type.ToString(),
                connectedDeviceCount: State.Devices?.Value.Length, transportState: "CONNECTED");
            StateChanged?.Invoke(State);
        }
    }
    private void Emit(SwitchDecision decision)
    {
        var previous = LastDecision;
        if (previous == decision) return;
        LastDecision = decision;
        log.Write("switch", decision.Action.ToString(), decision.Reason,
            previousState: previous?.Phase.ToString(), newState: decision.Phase.ToString(),
            audioSourceType: State.Source?.Value.Type.ToString(), connectedDeviceCount: State.Devices?.Value.Length,
            transportState: State.ControlConnected ? "CONNECTED" : "DISCONNECTED", requestId: decision.RequestId);
        Decision?.Invoke(decision);
    }
}
