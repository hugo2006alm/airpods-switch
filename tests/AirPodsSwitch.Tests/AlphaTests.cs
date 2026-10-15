// SPDX-License-Identifier: GPL-3.0-only
using System.IO.Compression;
using System.Text.Json;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Media;
using AirPodsSwitch.Core.Setup;

namespace AirPodsSwitch.Tests;

public sealed class AlphaTests
{
    [Fact]
    public void ActualAppIdentityOverridesProcessGuess()
    {
        Assert.Equal(AppMatch.ProcessAppId, AppIdentityMatcher.Match("Family!Player", "unusual", "Family!Player"));
        Assert.Equal(AppMatch.None, AppIdentityMatcher.Match("spotify.exe", "spotify", "Other!App"));
        Assert.Equal(AppMatch.ExecutableName, AppIdentityMatcher.Match("Spotify.EXE", "spotify", null));
    }

    [Theory]
    [InlineData("evilspotify", "spotify")]
    [InlineData("chromeevil", "chrome")]
    [InlineData("firefox-attacker", "firefox")]
    [InlineData("C:\\users\\person\\vlc.exe", "vlc")]
    [InlineData("Family!Player", "")]
    public void SimilarNamesAndPathsCannotConfirmRoute(string app, string process) =>
        Assert.Equal(AppMatch.None, AppIdentityMatcher.Match(app, process, null));

    private static SetupInputs Ready => new(true, true, true, true, true, "profile-driver", 1, 1);
    [Fact]
    public void SetupCannotConnectWithMissingEvidence()
    {
        Assert.True(SetupReadiness.Assess(Ready).CanConnect);
        foreach (var invalid in new[]
        {
            Ready with { RadioOn = false }, Ready with { RadioSelected = false },
            Ready with { DevicePaired = false }, Ready with { EndpointAvailable = false },
            Ready with { EndpointConfirmed = false }, Ready with { MatchingDriverInterfaces = 0 },
            Ready with { MatchingDriverInterfaces = 2 }, Ready with { RadioCount = 2 },
            Ready with { InventoryAvailable = false }, Ready with { Transport = "unexpected" }
        }) Assert.False(SetupReadiness.Assess(invalid).CanConnect);
        Assert.True(SetupReadiness.Assess(Ready with { Transport = "winsock-experimental", MatchingDriverInterfaces = 0 }).CanConnect);
    }

    [Fact]
    public void HealthLossExplainsExplicitRecovery()
    {
        Assert.Equal(ConnectionLoss.None, ConnectionHealth.Assess(false, true, true, true, true));
        Assert.Equal(ConnectionLoss.Suspended, ConnectionHealth.Assess(true, false, false, false, false));
        Assert.Equal(ConnectionLoss.InventoryUnavailable, ConnectionHealth.Assess(false, false, true, true, true));
        Assert.Equal(ConnectionLoss.RadioUnavailable, ConnectionHealth.Assess(false, true, false, true, true));
        Assert.Equal(ConnectionLoss.DeviceUnpaired, ConnectionHealth.Assess(false, true, true, false, true));
        Assert.Equal(ConnectionLoss.OutputUnavailable, ConnectionHealth.Assess(false, true, true, true, false));
    }

    [Fact]
    public void ExportReconstructsOnlyAllowlistedMetadata()
    {
        string? sanitized = DiagnosticsBundle.Sanitize(JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, subsystem = "transport", eventType = "closed",
            result = "secret key=DEADBEEF C:\\Users\\person\\backup.json AA:BB:CC:DD:EE:FF",
            audioSourceType = "secret", previousState = "ActiveWindowsMedia", transportState = "DISCONNECTED",
            connectedDeviceCount = 2, raw = "packet-data", takeoverRequestId = Guid.NewGuid()
        }));
        Assert.NotNull(sanitized);
        Assert.DoesNotContain("secret", sanitized);
        Assert.DoesNotContain("person", sanitized);
        Assert.DoesNotContain("packet-data", sanitized);
        Assert.DoesNotContain("takeoverRequestId", sanitized);
        Assert.Contains("ActiveWindowsMedia", sanitized);
        Assert.Null(DiagnosticsBundle.Sanitize("{broken"));
        Assert.Null(DiagnosticsBundle.Sanitize("[]"));
        Assert.Null(DiagnosticsBundle.Sanitize("{\"subsystem\":\"secret\",\"eventType\":\"closed\"}"));
    }

    [Fact]
    public void BundleNeverIncludesSettingsBackupOrRawLogAndRefusesOverwrite()
    {
        string folder = Path.Combine(Path.GetTempPath(), "airpods-bundle-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string log = Path.Combine(folder, "session.jsonl"), zip = Path.Combine(folder, "bundle.zip");
            File.WriteAllText(log, "{\"subsystem\":\"tray\",\"eventType\":\"session_failed\",\"result\":\"private-value\"}\n");
            var summary = new DiagnosticsSummary("0.1.0-alpha.1", "10.0.26200.0", "profile-driver",
                true, true, true, true, 1, 1, 1, 0, true, false);
            DiagnosticsBundle.Export(zip, summary, [log]);
            using var archive = ZipFile.OpenRead(zip);
            Assert.Equal(new[] { "summary.json", "README.txt", "events/1.jsonl" }, archive.Entries.Select(e => e.FullName));
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain("private-value", reader.ReadToEnd());
            }
            Assert.Throws<IOException>(() => DiagnosticsBundle.Export(zip, summary, [log]));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
