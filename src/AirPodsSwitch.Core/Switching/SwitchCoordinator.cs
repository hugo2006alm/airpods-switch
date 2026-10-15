// SPDX-License-Identifier: GPL-3.0-only
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;

namespace AirPodsSwitch.Core.Switching;

public enum SwitchPhase
{
    Unknown, AirPodsUnavailable, NotWorn, ConnectedControlOnly, ConnectedIdle,
    ActiveWindowsMedia, ActiveWindowsCall, ActiveOtherMedia, ActiveOtherCall,
    TakeoverAvailable, TakeoverRequested, TakeoverInProgress, TakeoverConfirmed,
    TakeoverFailed, Cooldown
}
public enum SwitchAction { None, Offer, SendTakeover, Cancel, Confirmed, Failed }
public sealed record MediaState(string SessionId, bool Meaningful, bool CallOrCaptureActive, long At);
public sealed record RenderEvidence(bool CorrelatedAirPodsRender, long At);
public sealed record SwitchInputs(AirPodsState AirPods, BluetoothAddress LocalAddress,
    MediaState Media, RenderEvidence Render);
public sealed record SwitchDecision(SwitchAction Action, SwitchPhase Phase, string Reason, Guid? RequestId = null);
public sealed record PendingTakeover(Guid Id, string SessionId, long Epoch, long OfferedAt,
    long? RequestedAt, long? Deadline, AudioSource PreviousSource, bool SawLocalSource = false);

/// <summary>Serialized pure policy. No Bluetooth/UI side effects; a send must be explicitly accepted.</summary>
public sealed class SwitchCoordinator
{
    private readonly HashSet<string> consumedSessions = new(StringComparer.Ordinal);
    private long cooldownUntil;
    public PendingTakeover? Pending { get; private set; }
    public SwitchPhase Phase { get; private set; } = SwitchPhase.Unknown;
    public long TimeoutMs { get; init; } = 12000;
    public long CooldownMs { get; init; } = 30000;

    public SwitchDecision Update(SwitchInputs input, long now)
    {
        if (Pending is { RequestedAt: not null } request)
        {
            if (!input.AirPods.ControlConnected || input.AirPods.Epoch != request.Epoch)
                return Finish(false, "Control transport changed", now);
            if (input.Media.CallOrCaptureActive || input.AirPods.Source?.Value.Type == AudioSourceType.Call)
                return Finish(false, "Call protection activated", now);
            if (!input.Media.Meaningful || input.Media.SessionId != request.SessionId ||
                input.Media.At > now || now - input.Media.At > 2000)
                return Finish(false, "Windows media stopped or changed", now);
            if (input.AirPods.Wear is not { } wear || !wear.Fresh(now, request.Epoch, 15000) || !wear.Value.IsWorn)
                return Finish(false, "Wear evidence lost", now);
            if (input.AirPods.Source is null || input.AirPods.Devices is null)
                return Finish(false, "Protocol state invalidated during takeover", now);
            if (input.AirPods.ReleaseAt >= request.RequestedAt)
                return Finish(false, "Other host requested release or reverse", now);
            if (now >= request.Deadline) return Finish(false, "Takeover timed out; previous source retained as evidence", now);
            var source = input.AirPods.Source;
            bool localSource = source is not null && source.Fresh(now, request.Epoch) &&
                source.At > request.RequestedAt && source.Value.Address == input.LocalAddress &&
                source.Value.Type == AudioSourceType.Media;
            if (request.SawLocalSource && source?.Value.Address != input.LocalAddress)
                return Finish(false, "Conflicting source after Windows source observation", now);
            if (localSource)
            {
                Pending = request with { SawLocalSource = true };
                if (input.Render.CorrelatedAirPodsRender && input.Render.At > request.RequestedAt &&
                    input.Render.At <= now && now - input.Render.At <= 2000)
                    return Finish(true, "Fresh Windows MEDIA source plus correlated AirPods render activity", now);
            }
            return Set(SwitchAction.None, SwitchPhase.TakeoverInProgress, "Awaiting source and audio-route evidence", request.Id);
        }

        bool eligible = Eligible(input, now, out string reason);
        if (Pending is { } offer)
        {
            if (!eligible || offer.Epoch != input.AirPods.Epoch || offer.SessionId != input.Media.SessionId ||
                now - offer.OfferedAt > 15000)
            {
                consumedSessions.Add(offer.SessionId);
                Pending = null;
                return Set(SwitchAction.Cancel, DerivePhase(input), reason, offer.Id);
            }
            return Set(SwitchAction.None, SwitchPhase.TakeoverAvailable, "Offer already visible", offer.Id);
        }
        if (!eligible) return Set(SwitchAction.None, DerivePhase(input), reason);
        if (now < cooldownUntil || consumedSessions.Contains(input.Media.SessionId))
            return Set(SwitchAction.None, SwitchPhase.Cooldown, "Session already handled or cooldown active");
        // Fail closed rather than allow unbounded process lifetime memory growth or reoffer old sessions.
        if (consumedSessions.Count >= 1024)
            return Set(SwitchAction.None, SwitchPhase.Cooldown, "Session history full; restart explicitly to reset");
        var id = Guid.NewGuid();
        Pending = new(id, input.Media.SessionId, input.AirPods.Epoch, now, null, null, input.AirPods.Source!.Value);
        return Set(SwitchAction.Offer, SwitchPhase.TakeoverAvailable, "Meaningful Windows media; AirPods worn and another host active/idle", id);
    }

    public SwitchDecision Accept(Guid id, SwitchInputs input, long now)
    {
        if (Pending is not { RequestedAt: null } offer || offer.Id != id)
            return new(SwitchAction.None, Phase, "Stale or duplicate notification action");
        // Notification latency must not bypass call/ear/source/transport checks.
        var validation = Update(input, now);
        if (Pending is null) return validation;
        consumedSessions.Add(offer.SessionId);
        Pending = offer with { RequestedAt = now, Deadline = checked(now + TimeoutMs) };
        return Set(SwitchAction.SendTakeover, SwitchPhase.TakeoverRequested, "User accepted; fresh state revalidated", id);
    }

    public SwitchDecision Reject(Guid id, long now)
    {
        if (Pending is not { RequestedAt: null } offer || offer.Id != id)
            return new(SwitchAction.None, Phase, "Stale notification dismissal");
        consumedSessions.Add(offer.SessionId);
        Pending = null;
        cooldownUntil = now + CooldownMs;
        return Set(SwitchAction.Cancel, SwitchPhase.Cooldown, "User rejected; suppress same session", id);
    }

    public SwitchDecision SendFailed(long now) => Pending is null
        ? new(SwitchAction.None, Phase, "No pending request")
        : Finish(false, "Transport write failed; no retry", now);

    private SwitchDecision Finish(bool success, string reason, long now)
    {
        var id = Pending?.Id;
        if (Pending is { } request) consumedSessions.Add(request.SessionId);
        Pending = null;
        cooldownUntil = now + CooldownMs;
        return Set(success ? SwitchAction.Confirmed : SwitchAction.Failed,
            success ? SwitchPhase.TakeoverConfirmed : SwitchPhase.TakeoverFailed, reason, id);
    }
    private static bool Eligible(SwitchInputs input, long now, out string reason)
    {
        var state = input.AirPods;
        reason = "Insufficient fresh protocol/media evidence";
        if (!input.LocalAddress.IsUsableHost || !state.ControlConnected || state.Identity?.Value.Model is null ||
            state.Identity.Epoch != state.Epoch || input.Media.At > now || now - input.Media.At > 2000 ||
            !input.Media.Meaningful || string.IsNullOrEmpty(input.Media.SessionId) || input.Media.CallOrCaptureActive)
            return false;
        if (state.Source is not { } source || !source.Fresh(now, state.Epoch) ||
            source.Value.Type == AudioSourceType.Call || source.Value.Address == input.LocalAddress)
        { reason = "Source unknown, stale, local or in call"; return false; }
        if (state.Wear is not { } wear || !wear.Fresh(now, state.Epoch, 15000) || !wear.Value.IsWorn)
        { reason = "AirPods wear state not fresh/in ear"; return false; }
        if (state.Devices is not { } hosts || !hosts.Fresh(now, state.Epoch, 15000) ||
            !hosts.Value.Any(d => d.Address == input.LocalAddress) ||
            !hosts.Value.Any(d => d.Address != input.LocalAddress) ||
            source.Value.Type == AudioSourceType.Media && !hosts.Value.Any(d => d.Address == source.Value.Address))
        { reason = "Useful two-host state not observed"; return false; }
        if (state.ReleaseAt is { } release && now - release < 15000)
        { reason = "Recent reverse/release request"; return false; }
        reason = "Eligible";
        return true;
    }
    private static SwitchPhase DerivePhase(SwitchInputs input)
    {
        var state = input.AirPods;
        if (!state.ControlConnected) return SwitchPhase.AirPodsUnavailable;
        if (state.Wear?.Value.IsWorn == false) return SwitchPhase.NotWorn;
        if (state.Source is null) return SwitchPhase.ConnectedControlOnly;
        bool local = state.Source.Value.Address == input.LocalAddress;
        return state.Source.Value.Type switch
        {
            AudioSourceType.Call => local ? SwitchPhase.ActiveWindowsCall : SwitchPhase.ActiveOtherCall,
            AudioSourceType.Media => local ? SwitchPhase.ActiveWindowsMedia : SwitchPhase.ActiveOtherMedia,
            _ => SwitchPhase.ConnectedIdle
        };
    }
    private SwitchDecision Set(SwitchAction action, SwitchPhase phase, string reason, Guid? id = null)
    {
        Phase = phase;
        return new(action, phase, reason, id);
    }
}
