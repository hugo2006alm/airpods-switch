// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AirPodsSwitch.Windows.Media;

public sealed record AudioEndpoint(string Id, string Name, string InstanceId);
public sealed record RenderSession(string EndpointId, string ProcessName, uint ProcessId, bool IsSystemSound, float Peak);
public sealed record AudioSnapshot(IReadOnlyList<AudioEndpoint> Endpoints, IReadOnlyList<RenderSession> Render,
    bool CaptureActive, bool CommunicationAppActive);

/// <summary>Read-only Core Audio observer. Every COM wrapper is created/released on this call's thread.</summary>
public static class WindowsAudioManager
{
    private static readonly HashSet<string> CommunicationApps = new(StringComparer.OrdinalIgnoreCase)
        { "teams", "ms-teams", "zoom", "discord", "skype", "slack", "webex", "whatsapp" };

    public static AudioSnapshot Read()
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = new List<AudioEndpoint>();
        var render = new List<RenderSession>();
        bool capture = false, communications = false;
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                endpoints.Add(new(device.ID, device.FriendlyName, device.InstanceId));
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                    uint pid = session.GetProcessID;
                    string process = ProcessName(pid);
                    bool system = session.IsSystemSoundsSession;
                    float peak = session.AudioMeterInformation.MasterPeakValue;
                    render.Add(new(device.ID, process, pid, system, peak));
                    if (!system && CommunicationApps.Contains(process)) communications = true;
                }
            }
        }
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateActive) capture = true;
                }
            }
        }
        return new(endpoints, render, capture, communications);
    }

    public static bool MatchesApp(string appId, string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var app = appId.ToLowerInvariant();
        var process = processName.ToLowerInvariant();
        if (app == process || app == process + ".exe") return true;
        return process switch
        {
            "spotify" => app.Contains("spotify", StringComparison.Ordinal),
            "msedge" => app.Contains("microsoftedge", StringComparison.Ordinal) || app.StartsWith("msedge", StringComparison.Ordinal),
            "chrome" => app.StartsWith("chrome", StringComparison.Ordinal),
            "firefox" => app.StartsWith("firefox", StringComparison.Ordinal),
            "vlc" => app.Contains("videolan", StringComparison.Ordinal) || app == "vlc.exe",
            _ => false // unresolved AUMID/PID mapping cannot confirm a route
        };
    }
    private static string ProcessName(uint id)
    {
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName; }
        catch (ArgumentException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
}
