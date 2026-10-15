// SPDX-License-Identifier: GPL-3.0-only
namespace AirPodsSwitch.Core.Media;

public enum AppMatch { None, ProcessAppId, ExecutableName, KnownDesktopId }

/// <summary>Prefer OS identities; never treat an arbitrary substring as an application match.</summary>
public static class AppIdentityMatcher
{
    public static AppMatch Match(string sourceAppId, string processName, string? processAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId) || string.IsNullOrWhiteSpace(processName)) return AppMatch.None;
        // A known, conflicting OS identity must never fall back to a process-name guess.
        if (!string.IsNullOrEmpty(processAppId))
            return string.Equals(sourceAppId, processAppId, StringComparison.OrdinalIgnoreCase)
                ? AppMatch.ProcessAppId : AppMatch.None;
        if (string.Equals(sourceAppId, processName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceAppId, processName + ".exe", StringComparison.OrdinalIgnoreCase))
            return AppMatch.ExecutableName;
        return (processName.ToLowerInvariant(), sourceAppId.ToLowerInvariant()) switch
        {
            ("msedge", "microsoftedge") => AppMatch.KnownDesktopId,
            ("vlc", "videolan.vlc") => AppMatch.KnownDesktopId,
            _ => AppMatch.None
        };
    }
}
