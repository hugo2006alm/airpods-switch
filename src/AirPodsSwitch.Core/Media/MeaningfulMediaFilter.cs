// SPDX-License-Identifier: GPL-3.0-or-later
using AirPodsSwitch.Core.Switching;

namespace AirPodsSwitch.Core.Media;

public sealed record MediaPlayback(string AppId, bool Playing);
public sealed record MediaCandidate(string AppId, string SessionId, long StartedAt);

/// <summary>SMTC playback must persist; system audio sessions alone can never create an offer.</summary>
public sealed class MeaningfulMediaFilter
{
    private readonly Dictionary<string, MediaCandidate> running = new(StringComparer.Ordinal);
    public long MinimumPlaybackMs { get; init; } = 2000;
    public (MediaState State, string? AppId) Observe(IEnumerable<MediaPlayback> playback, bool callOrCapture, long now)
    {
        var playing = playback.Where(p => p.Playing).Select(p => p.AppId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        foreach (string id in running.Keys.Where(id => !playing.Contains(id)).ToArray()) running.Remove(id);
        foreach (string id in playing)
        {
            if (!running.ContainsKey(id)) running[id] = new(id, Guid.NewGuid().ToString("N"), now);
        }
        var candidate = running.Values.OrderBy(c => c.StartedAt).ThenBy(c => c.AppId, StringComparer.Ordinal).FirstOrDefault();
        return (new(candidate?.SessionId ?? "", candidate is not null && now - candidate.StartedAt >= MinimumPlaybackMs,
            callOrCapture, now), candidate?.AppId);
    }
}
