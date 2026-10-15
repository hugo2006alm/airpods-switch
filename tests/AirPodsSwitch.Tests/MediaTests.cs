// SPDX-License-Identifier: GPL-3.0-or-later
using AirPodsSwitch.Core.Media;

namespace AirPodsSwitch.Tests;

public sealed class MediaTests
{
    [Fact]
    public void RapidPlaybackAndDuplicatesNeverSkipMinimumDuration()
    {
        var filter = new MeaningfulMediaFilter();
        var playback = new[] { new MediaPlayback("player", true), new MediaPlayback("player", true) };
        Assert.False(filter.Observe(playback, false, 100).State.Meaningful);
        Assert.False(filter.Observe(playback, false, 1000).State.Meaningful);
        Assert.True(filter.Observe(playback, false, 2100).State.Meaningful);
    }
    [Fact]
    public void StopAndRestartCreateNewSessionAndDebounceAgain()
    {
        var filter = new MeaningfulMediaFilter();
        var playback = new[] { new MediaPlayback("player", true) };
        string first = filter.Observe(playback, false, 100).State.SessionId;
        filter.Observe([], false, 200);
        var resumed = filter.Observe(playback, false, 300);
        Assert.NotEqual(first, resumed.State.SessionId);
        Assert.False(resumed.State.Meaningful);
    }
    [Fact]
    public void EmptySMTCNeverCreatesMediaFromNotificationAudio()
    {
        var filter = new MeaningfulMediaFilter();
        Assert.False(filter.Observe([], false, 100000).State.Meaningful);
    }
    [Fact]
    public void CallsAreConservativeVeto()
    {
        var filter = new MeaningfulMediaFilter { MinimumPlaybackMs = 0 };
        Assert.True(filter.Observe([new("player", true)], true, 100).State.CallOrCaptureActive);
    }
}
