// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;
using AirPodsSwitch.Windows.Bluetooth;
using AirPodsSwitch.Windows.Media;
using AirPodsSwitch.Windows.Switching;
using EventLog = AirPodsSwitch.Core.Diagnostics.EventLog;

namespace AirPodsSwitch.Tray;

internal sealed class TrayForm : Form
{
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }
    private readonly NotifyIcon icon = new() { Icon = SystemIcons.Application, Text = "AirPods Switch", Visible = true };
    private readonly ComboBox radios = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390 };
    private readonly ComboBox devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390 };
    private readonly ComboBox endpoints = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390 };
    private readonly ComboBox transport = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 390 };
    private readonly TextBox hostName = new() { Width = 390 };
    private readonly CheckBox notify = new() { Text = "Offer switch notifications", Checked = true, AutoSize = true };
    private readonly CheckBox protocolDebug = new() { Text = "Protocol debug metadata (payloads stay redacted)", AutoSize = true };
    private readonly Label status = new() { Text = "Experimental. Connect to observe AirPods state.", AutoSize = true, MaximumSize = new Size(500, 0) };
    private readonly Button connect = new() { Text = "Connect", AutoSize = true };
    private readonly Button stop = new() { Text = "Stop", Enabled = false, AutoSize = true };
    private readonly Button switchNow = new() { Text = "Switch", Enabled = false, AutoSize = true };
    private readonly Button reject = new() { Text = "Not now", Enabled = false, AutoSize = true };
    private readonly Button refresh = new() { Text = "Refresh devices", AutoSize = true };
    private readonly Button diagnostics = new() { Text = "Open diagnostics folder", AutoSize = true };
    private TraySettings? settings;
    private NotificationManager? notifications;
    private LiveController? controller;
    private CancellationTokenSource? running;
    private Task? worker;
    private Guid? offeredId;
    private bool quitting;
    private string? notificationError;

    public TrayForm()
    {
        Text = "AirPods Switch — Experimental";
        ClientSize = new Size(560, 520);
        MinimumSize = new Size(580, 560);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        AddField(layout, "This PC's label", hostName);
        AddField(layout, "Bluetooth radio", radios);
        AddField(layout, "Paired AirPods", devices);
        AddField(layout, "AirPods audio output (confirm in Windows Sound settings)", endpoints);
        transport.Items.AddRange(new object[] { "profile-driver", "winsock-experimental" });
        transport.SelectedIndex = 0;
        AddField(layout, "Control transport", transport);
        layout.Controls.Add(notify);
        layout.Controls.Add(protocolDebug);
        var buttons = new FlowLayoutPanel { Width = 510, Height = 72, AutoSize = false };
        buttons.Controls.AddRange([connect, stop, switchNow, reject, refresh, diagnostics]);
        layout.Controls.Add(buttons);
        layout.Controls.Add(status);
        Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status and settings", null, (_, _) => ShowWindow());
        menu.Items.Add("Switch", null, (_, _) => Respond(true));
        menu.Items.Add("Diagnostics", null, (_, _) => OpenDiagnostics());
        menu.Items.Add("Quit", null, async (_, _) => await QuitAsync());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => ShowWindow();
        radios.SelectedIndexChanged += (_, _) => PopulateDevices();
        connect.Click += async (_, _) =>
        {
            try { await StartAsync(); }
            catch (Exception error) { status.Text = "Start failed: " + error.Message; }
        };
        stop.Click += async (_, _) => await StopAsync();
        switchNow.Click += (_, _) => Respond(true);
        reject.Click += (_, _) => Respond(false);
        refresh.Click += async (_, _) => await RefreshAsync();
        diagnostics.Click += (_, _) => OpenDiagnostics();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.WindowsShutDown) { running?.Cancel(); return; }
            if (!quitting) { e.Cancel = true; Hide(); }
        };
        Shown += async (_, _) =>
        {
            try
            {
                settings = TraySettings.Load();
                hostName.Text = settings?.FriendlyHostName ?? Environment.MachineName;
                notify.Checked = settings?.Notifications ?? true;
                transport.SelectedItem = settings?.Transport ?? "profile-driver";
                notifications = new NotificationManager();
                notifications.Action += action => controller?.Post(action);
            }
            catch (Exception error) { notificationError = error.GetType().Name; }
            await RefreshAsync();
        };
    }
    private static void AddField(Control parent, string title, Control control)
    {
        parent.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 8, 0, 1) });
        parent.Controls.Add(control);
    }
    private async Task RefreshAsync()
    {
        if (running is not null) return;
        refresh.Enabled = false;
        try
        {
            var inventory = await Task.Run(BluetoothInventory.Read);
            var audio = await Task.Run(WindowsAudioManager.Read);
            radios.Items.Clear();
            foreach (var radio in inventory) radios.Items.Add(new Choice<BluetoothRadio>(radio.Name, radio));
            Select<BluetoothRadio>(radios, settings?.RadioAddress, c => c.Value.Address);
            endpoints.Items.Clear();
            foreach (var endpoint in audio.Endpoints) endpoints.Items.Add(new Choice<AudioEndpoint>(endpoint.Name, endpoint));
            Select<AudioEndpoint>(endpoints, settings?.EndpointId, c => c.Value.Id);
            status.Text = notificationError is null ? "Select paired AirPods and matching audio output. No DID changes occur." :
                $"Native notifications unavailable ({notificationError}). Manual Switch remains available for eligible state.";
        }
        catch (Exception error) { status.Text = "Inventory failed: " + error.Message; }
        finally { refresh.Enabled = true; }
    }
    private static void Select<T>(ComboBox box, string? saved, Func<Choice<T>, string> key)
    {
        var items = box.Items.Cast<Choice<T>>().ToArray();
        box.SelectedItem = items.FirstOrDefault(c => key(c) == saved);
        if (box.SelectedIndex < 0 && items.Length == 1) box.SelectedIndex = 0;
    }
    private void PopulateDevices()
    {
        devices.Items.Clear();
        if (radios.SelectedItem is not Choice<BluetoothRadio> radio) return;
        foreach (var device in radio.Value.Devices.Where(d => d.NameSuggestsAirPods || (d.ClassOfDevice & 0x1F00) == 0x0400))
            devices.Items.Add(new Choice<PairedDevice>(device.Name, device));
        Select<PairedDevice>(devices, settings?.DeviceAddress, c => c.Value.Address);
    }
    private async Task StartAsync()
    {
        if (running is not null) return;
        if (radios.SelectedItem is not Choice<BluetoothRadio> radio || devices.SelectedItem is not Choice<PairedDevice> device ||
            endpoints.SelectedItem is not Choice<AudioEndpoint> endpoint)
        { status.Text = "Pair AirPods in Windows, then select radio, AirPods and matching audio output."; return; }
        settings = new(settings?.HostId ?? Guid.NewGuid(), hostName.Text, radio.Value.Address,
            device.Value.Address, endpoint.Value.Id, transport.Text, notify.Checked);
        settings.Save();
        Directory.CreateDirectory(TraySettings.DirectoryPath);
        running = new CancellationTokenSource();
        var cancel = running;
        var logPath = Path.Combine(TraySettings.DirectoryPath, "session-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
        var writer = new SessionLogWriter(logPath);
        var log = new EventLog(writer, protocolDebug.Checked);
        controller = new(new(BluetoothAddress.Parse(settings.DeviceAddress), BluetoothAddress.Parse(settings.RadioAddress),
            settings.EndpointId, settings.Transport, OfferSwitch: true), log);
        var activeController = controller;
        controller.StateChanged += state => Dispatch(() => UpdateStatus(state));
        controller.Decision += decision => Dispatch(() => HandleDecision(decision, log));
        SetRunning(true);
        worker = Task.Run(async () =>
        {
            try { await activeController.RunAsync(cancel.Token); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception error)
            {
                log.Write("tray", "session_failed", error.GetType().Name);
                Dispatch(() => status.Text = "Control session unavailable: " + error.Message);
            }
            finally
            {
                await writer.DisposeAsync();
                Dispatch(() =>
                {
                    if (!ReferenceEquals(controller, activeController)) return;
                    running?.Dispose(); running = null; worker = null; controller = null;
                    SetRunning(false); offeredId = null; switchNow.Enabled = reject.Enabled = false;
                });
            }
        });
        await Task.CompletedTask;
    }
    private void SetRunning(bool value)
    {
        connect.Enabled = refresh.Enabled = radios.Enabled = devices.Enabled = endpoints.Enabled = transport.Enabled = !value;
        stop.Enabled = value;
    }
    private void UpdateStatus(AirPodsState state)
    {
        string model = state.Identity?.Value.Model?.Name ?? "AirPods";
        string source = state.Source is null ? "Unknown" : state.Source.Value.Type == AudioSourceType.None ? "Idle" :
            state.Source.Value.Address.ToString() == settings?.RadioAddress ? "This PC" : "Another device";
        status.Text = state.ControlConnected ? $"{model} • {source} • {state.Source?.Value.Type.ToString() ?? "Unknown"} • {state.Devices?.Value.Length.ToString() ?? "?"} connected hosts" :
            "Control session closed. Connect explicitly to retry.";
    }
    private void HandleDecision(SwitchDecision decision, EventLog log)
    {
        if (decision.Action == SwitchAction.Offer && decision.RequestId is { } id)
        {
            offeredId = id;
            switchNow.Enabled = reject.Enabled = true;
            if (notify.Checked && notifications is not null)
            {
                try { notifications.Offer(id); }
                catch (Exception error) { log.Write("notification", "failed", error.GetType().Name); status.Text = "Notification unavailable. Use Switch here."; }
            }
        }
        if (decision.Action is SwitchAction.Cancel or SwitchAction.Confirmed or SwitchAction.Failed or SwitchAction.SendTakeover)
        {
            offeredId = null;
            switchNow.Enabled = reject.Enabled = false;
            if (notifications is not null) _ = ClearNotificationsAsync();
        }
        if (decision.Action is SwitchAction.Confirmed or SwitchAction.Failed)
            status.Text = decision.Action == SwitchAction.Confirmed ? "Windows source and render confirmed. Verify audible playback." : "Switch not confirmed. No automatic retry.";
    }
    private async Task ClearNotificationsAsync()
    {
        try { if (notifications is not null) await notifications.ClearAsync(); }
        catch { status.Text = "Notification could not be removed. Stale actions remain blocked."; }
    }
    private void Respond(bool accept)
    {
        if (offeredId is { } id) controller?.Post(new(id, accept));
    }
    private async Task StopAsync()
    {
        running?.Cancel();
        if (worker is not null) await worker;
        running?.Dispose(); running = null; worker = null; controller = null;
        await ClearNotificationsAsync();
        SetRunning(false);
    }
    private async Task QuitAsync()
    {
        await StopAsync();
        notifications?.Dispose();
        icon.Visible = false; icon.Dispose();
        quitting = true; Close();
    }
    private void Dispatch(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static void OpenDiagnostics()
    {
        Directory.CreateDirectory(TraySettings.DirectoryPath);
        Process.Start(new ProcessStartInfo(TraySettings.DirectoryPath) { UseShellExecute = true });
    }
}
