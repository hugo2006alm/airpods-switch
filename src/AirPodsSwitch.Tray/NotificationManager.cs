// SPDX-License-Identifier: GPL-3.0-only
using AirPodsSwitch.Core.Switching;
using AirPodsSwitch.Windows.Switching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AirPodsSwitch.Tray;

internal sealed class NotificationManager : IDisposable
{
    private string? currentTag;
    public event Action<UserDecision>? Action;
    public NotificationManager()
    {
        AppNotificationManager.Default.NotificationInvoked += Invoked;
        try { AppNotificationManager.Default.Register(); }
        catch { AppNotificationManager.Default.NotificationInvoked -= Invoked; throw; }
    }
    public void Offer(Guid requestId)
    {
        var notification = new AppNotificationBuilder()
            .AddText("AirPods 5")
            .AddText("Audio is currently coming from another device. Use AirPods on this PC?")
            .AddButton(new AppNotificationButton("Not now").AddArgument("action", "reject").AddArgument("requestId", requestId.ToString()))
            .AddButton(new AppNotificationButton("Switch").AddArgument("action", "switch").AddArgument("requestId", requestId.ToString()))
            .BuildNotification();
        notification.Tag = requestId.ToString("N");
        currentTag = notification.Tag;
        AppNotificationManager.Default.Show(notification);
    }
    private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("requestId", out string? raw) && Guid.TryParse(raw, out var requestId) &&
            args.Arguments.TryGetValue("action", out string? action) && action is "switch" or "reject")
            Action?.Invoke(new(requestId, action == "switch"));
    }
    public async Task ClearAsync()
    {
        // Late cleanup from an interrupted connection must not remove a newer offer.
        string? tag = Interlocked.Exchange(ref currentTag, null);
        if (tag is not null) await AppNotificationManager.Default.RemoveByTagAsync(tag);
    }
    public void Dispose()
    {
        AppNotificationManager.Default.NotificationInvoked -= Invoked;
        AppNotificationManager.Default.Unregister();
    }
}
