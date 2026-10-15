// SPDX-License-Identifier: GPL-3.0-only
using AirPodsSwitch.Core.Media;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;
using Windows.Media.Control;

namespace AirPodsSwitch.Windows.Media;

public sealed record WindowsMediaSnapshot(MediaState State, string? AppId,
    IReadOnlyList<MediaPlayback> Playback, AudioSnapshot Audio, RenderEvidence Render);

public sealed class WindowsMediaMonitor
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly MeaningfulMediaFilter filter = new();
    public async Task<WindowsMediaSnapshot> SampleAsync(string? selectedEndpoint, CancellationToken cancellationToken)
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        var playback = manager.GetSessions().Select(s => new MediaPlayback(s.SourceAppUserModelId,
            s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)).ToArray();
        // Core Audio synchronous observation stays on one worker thread, with no retained COM wrappers.
        var audio = await Task.Run(WindowsAudioManager.Read, cancellationToken);
        long at = MonotonicClock.Milliseconds;
        var (state, appId) = filter.Observe(playback, audio.CaptureActive || audio.CommunicationAppActive, at);
        bool correlated = selectedEndpoint is not null && appId is not null && audio.Render.Any(r =>
            r.EndpointId == selectedEndpoint && !r.IsSystemSound && r.Peak > 0.0001f && WindowsAudioManager.MatchesApp(appId, r.ProcessName));
        return new(state, appId, playback, audio, new(correlated, at));
    }
}
