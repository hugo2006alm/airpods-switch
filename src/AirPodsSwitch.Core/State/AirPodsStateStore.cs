// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Immutable;
using System.Diagnostics;
using AirPodsSwitch.Core.Protocol;

namespace AirPodsSwitch.Core.State;

public static class MonotonicClock
{
    public static long Milliseconds => (long)Stopwatch.GetElapsedTime(0).TotalMilliseconds;
}
public sealed record Observation<T>(T Value, long At, long Epoch)
{
    public bool Fresh(long now, long epoch, long maximumAgeMs = 5000) =>
        Epoch == epoch && now >= At && now - At <= maximumAgeMs;
}
public sealed record AirPodsState
{
    public long Epoch { get; init; }
    public bool ControlConnected { get; init; }
    public Observation<DeviceIdentity>? Identity { get; init; }
    public Observation<AudioSource>? Source { get; init; }
    public Observation<ImmutableArray<ConnectedDevice>>? Devices { get; init; }
    public Observation<WearState>? Wear { get; init; }
    public Observation<bool>? OwnsConnection { get; init; }
    public Observation<ImmutableArray<BatteryState>>? Batteries { get; init; }
    public long? ReleaseAt { get; init; }
}

/// <summary>Single serialized event owner. Never update the store optimistically on transmit.</summary>
public sealed class AirPodsStateStore
{
    public AirPodsState Current { get; private set; } = new();
    public long BeginConnection()
    {
        Current = new AirPodsState { Epoch = Current.Epoch + 1, ControlConnected = true };
        return Current.Epoch;
    }
    public void Disconnect() => Current = new AirPodsState { Epoch = Current.Epoch + 1 };
    public bool Apply(ProtocolEvent value, long at, long epoch)
    {
        if (epoch != Current.Epoch || !Current.ControlConnected || at < 0) return false;
        var state = Current;
        switch (value)
        {
            case ProtocolStateInvalidated:
                Current = state with { Source = null, Devices = null, Wear = null, OwnsConnection = null };
                return true;
            case SourceReceived e when state.Source is null || at >= state.Source.At:
                Current = state with { Source = new(e.Source, at, epoch) }; return true;
            case DevicesReceived e when state.Devices is null || at >= state.Devices.At:
                Current = state with { Devices = new(e.Devices, at, epoch) }; return true;
            case WearReceived e when state.Wear is null || at >= state.Wear.At:
                Current = state with { Wear = new(e.Wear, at, epoch) }; return true;
            case OwnershipReceived e when state.OwnsConnection is null || at >= state.OwnsConnection.At:
                Current = state with { OwnsConnection = new(e.Owns, at, epoch) }; return true;
            case IdentityReceived e when state.Identity is null || at >= state.Identity.At:
                Current = state with { Identity = new(e.Identity, at, epoch) }; return true;
            case BatteryReceived e when state.Batteries is null || at >= state.Batteries.At:
                Current = state with { Batteries = new(e.Batteries, at, epoch) }; return true;
            case RoutingReceived { Release: true } when state.ReleaseAt is null || at >= state.ReleaseAt:
                Current = state with { ReleaseAt = at }; return true;
            default: return false;
        }
    }
}
