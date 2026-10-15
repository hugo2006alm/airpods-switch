// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Reflection;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.Setup;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;
using AirPodsSwitch.Windows.Bluetooth;
using AirPodsSwitch.Windows.Media;
using AirPodsSwitch.Windows.Switching;
using Microsoft.Win32;
using EventLog = AirPodsSwitch.Core.Diagnostics.EventLog;

namespace AirPodsSwitch.Tray;

internal sealed class TrayForm : Form
{
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }
    private readonly NotifyIcon icon = new() { Icon = SystemIcons.Application, Text = "AirPods Switch", Visible = true };
    private readonly ComboBox radios = Picker(), devices = Picker(), endpoints = Picker(), transport = Picker();
    private readonly TextBox hostName = new() { Width = 450, MaxLength = 80 };
    private readonly CheckBox confirmedOutput = new() { Text = "I confirmed this output belongs to the selected AirPods", AutoSize = true };
    private readonly CheckBox notify = new() { Text = "Offer switch notifications", Checked = true, AutoSize = true };
    private readonly CheckBox protocolDebug = new() { Text = "Protocol debug metadata (payloads stay redacted)", AutoSize = true };
    private readonly Label setup = new() { AutoSize = true, MaximumSize = new Size(510, 0) };
    private readonly Label status = new() { Text = "Refresh devices to begin setup.", AutoSize = true, MaximumSize = new Size(510, 0) };
    private readonly Button connect = Button("Connect"), stop = Button("Stop"), switchNow = Button("Switch"), reject = Button("Not now");
    private readonly Button refresh = Button("Refresh devices"), export = Button("Export diagnostics…");
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private TraySettings? settings;
    private SetupInventory? inventory;
    private NotificationManager? notifications;
    private LiveController? controller;
    private CancellationTokenSource? running;
    private Task? worker;
    private Guid? offeredId;
    private bool quitting, suspended, refreshing;
    private int setupGeneration;
    private string? notificationError;

    public TrayForm()
    {
        Text = "AirPods Switch — Alpha " + AppVersion;
        ClientSize = new Size(590, 730);
        MinimumSize = new Size(610, 600);
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        layout.Controls.Add(new Label { Text = "Set up this PC", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        AddField(layout, "This PC's label", hostName);
        AddField(layout, "1. Bluetooth radio", radios);
        AddField(layout, "2. Paired AirPods", devices);
        AddField(layout, "3. AirPods audio output", endpoints);
        layout.Controls.Add(confirmedOutput);
        transport.Items.AddRange(new object[] {
            new Choice<string>("Installed control driver", "profile-driver"),
            new Choice<string>("Direct Bluetooth (experimental)", "winsock-experimental") });
        transport.SelectedIndex = 0;
        AddField(layout, "4. Control connection", transport);
        layout.Controls.Add(setup);
        var help = new FlowLayoutPanel { Width = 530, AutoSize = true };
        Button bluetooth = Button("Bluetooth settings"), sound = Button("Sound settings"), guide = Button("Setup guide");
        bluetooth.Click += (_, _) => Open("ms-settings:bluetooth");
        sound.Click += (_, _) => Open("ms-settings:sound");
        guide.Click += (_, _) => OpenSetupGuide();
        help.Controls.AddRange([bluetooth, sound, guide]);
        layout.Controls.Add(help);
        layout.Controls.Add(notify);
        layout.Controls.Add(protocolDebug);
        var buttons = new FlowLayoutPanel { Width = 530, AutoSize = true };
        buttons.Controls.AddRange([connect, stop, switchNow, reject, refresh, export]);
        layout.Controls.Add(buttons);
        layout.Controls.Add(status);
        Controls.Add(layout);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status and settings", null, (_, _) => ShowWindow());
        menu.Items.Add("Switch", null, (_, _) => Respond(true));
        menu.Items.Add("Export diagnostics…", null, async (_, _) => await ExportAsync());
        menu.Items.Add("Quit", null, async (_, _) => await QuitAsync());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => ShowWindow();
        radios.SelectedIndexChanged += (_, _) => { confirmedOutput.Checked = false; PopulateDevices(); UpdateSetup(); };
        devices.SelectedIndexChanged += (_, _) => { confirmedOutput.Checked = false; UpdateSetup(); };
        endpoints.SelectedIndexChanged += (_, _) => { confirmedOutput.Checked = false; UpdateSetup(); };
        transport.SelectedIndexChanged += (_, _) => UpdateSetup();
        confirmedOutput.CheckedChanged += (_, _) => UpdateSetup();
        connect.Click += async (_, _) => await StartAsync();
        stop.Click += async (_, _) => await StopAsync("Stopped. Connect explicitly to start a fresh session.");
        switchNow.Click += (_, _) => Respond(true);
        reject.Click += (_, _) => Respond(false);
        refresh.Click += async (_, _) => await RefreshAsync();
        export.Click += async (_, _) => await ExportAsync();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.WindowsShutDown) { running?.Cancel(); return; }
            if (!quitting) { e.Cancel = true; Hide(); }
        };
        FormClosed += (_, _) => { SystemEvents.PowerModeChanged -= PowerChanged; running?.Cancel(); icon.Dispose(); };
        SystemEvents.PowerModeChanged += PowerChanged;
        Shown += async (_, _) =>
        {
            try { settings = TraySettings.Load(); }
            catch { status.Text = "Saved settings could not be read. Select devices again; the file is unchanged."; }
            hostName.Text = settings?.FriendlyHostName ?? "This PC";
            notify.Checked = settings?.Notifications ?? true;
            Select<string>(transport, settings?.Transport ?? "profile-driver", c => c.Value);
            try
            {
                notifications = new NotificationManager();
                notifications.Action += action => controller?.Post(action);
            }
            catch (Exception error) { notificationError = error.GetType().Name; }
            await RefreshAsync();
        };
        ClearOffer(); SetRunning(false);
    }

    private static ComboBox Picker() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 450 };
    private static Button Button(string text) => new() { Text = text, AutoSize = true };
    private static string AppVersion => typeof(TrayForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private string SelectedTransport => (transport.SelectedItem as Choice<string>)?.Value ?? "profile-driver";
    private string? SelectedRadio => (radios.SelectedItem as Choice<BluetoothRadio>)?.Value.Address;
    private string? SelectedDevice => (devices.SelectedItem as Choice<PairedDevice>)?.Value.Address;
    private string? SelectedOutput => (endpoints.SelectedItem as Choice<AudioEndpoint>)?.Value.Id;
    private static void AddField(Control parent, string title, Control control)
    {
        parent.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 8, 0, 1) });
        parent.Controls.Add(control);
    }
    private SetupReadiness Readiness() => SetupReadiness.Assess(new(
        SelectedRadio is not null, inventory?.RadioOn(SelectedRadio) == true,
        devices.SelectedItem is Choice<PairedDevice> d && (d.Value.Authenticated || d.Value.Remembered),
        SelectedOutput is not null && inventory?.Outputs.Any(e => e.Id == SelectedOutput) == true,
        confirmedOutput.Checked, SelectedTransport, inventory?.MatchingDrivers(SelectedDevice) ?? 0,
        inventory?.Radios.Count ?? 0, inventory is not null));
    private void UpdateSetup()
    {
        var readiness = Readiness();
        setup.Text = readiness.Instruction;
        connect.Enabled = running is null && !refreshing && !suspended && !quitting && readiness.CanConnect;
    }
    private async Task RefreshAsync()
    {
        if (running is not null || refreshing || quitting) return;
        refreshing = true; refresh.Enabled = false; connect.Enabled = false;
        int generation = ++setupGeneration;
        // Preserve choices made this launch, not just the last saved connection.
        string? radio = SelectedRadio ?? settings?.RadioAddress, device = SelectedDevice ?? settings?.DeviceAddress,
            output = SelectedOutput ?? settings?.EndpointId;
        try
        {
            var refreshed = await SetupInventory.ReadAsync(CancellationToken.None);
            if (generation != setupGeneration || suspended || quitting) return;
            inventory = refreshed;
            radios.Items.Clear();
            foreach (var item in inventory.Radios) radios.Items.Add(new Choice<BluetoothRadio>(item.Name, item));
            Select<BluetoothRadio>(radios, radio, c => c.Value.Address);
            Select<PairedDevice>(devices, device, c => c.Value.Address);
            endpoints.Items.Clear();
            foreach (var item in inventory.Outputs) endpoints.Items.Add(new Choice<AudioEndpoint>(item.Name, item));
            Select<AudioEndpoint>(endpoints, output, c => c.Value.Id);
            confirmedOutput.Checked = false;
            status.Text = notificationError is null ? "Setup never changes DID or installs drivers. Switching still requires fresh AirPods state and your acceptance." :
                "Native notifications are unavailable. Eligible switch requests can still be accepted in this window.";
        }
        catch { inventory = null; status.Text = "Device refresh failed. Check Bluetooth and Windows Audio, then try Refresh devices."; }
        finally { refreshing = false; refresh.Enabled = running is null; UpdateSetup(); }
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
        foreach (var device in radio.Value.Devices.Where(d => (d.Authenticated || d.Remembered) &&
            (d.NameSuggestsAirPods || (d.ClassOfDevice & 0x1F00) == 0x0400)))
            devices.Items.Add(new Choice<PairedDevice>(device.Name, device));
        Select<PairedDevice>(devices, settings?.DeviceAddress, c => c.Value.Address);
    }
    private async Task StartAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (running is not null || refreshing || suspended || quitting || !Readiness().CanConnect) { UpdateSetup(); return; }
            settings = new(settings?.HostId ?? Guid.NewGuid(), hostName.Text.Trim(), SelectedRadio!, SelectedDevice!,
                SelectedOutput!, SelectedTransport, notify.Checked);
            settings.Save();
            var cancel = new CancellationTokenSource();
            var path = Path.Combine(TraySettings.DirectoryPath, "session-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
            var writer = new SessionLogWriter(path);
            var log = new EventLog(writer, protocolDebug.Checked);
            var active = new LiveController(new(BluetoothAddress.Parse(settings.DeviceAddress), BluetoothAddress.Parse(settings.RadioAddress),
                settings.EndpointId, settings.Transport, OfferSwitch: true), log);
            running = cancel; controller = active;
            active.StateChanged += state => Dispatch(() => { if (ReferenceEquals(controller, active)) UpdateStatus(state); });
            active.Decision += decision => Dispatch(() => { if (ReferenceEquals(controller, active)) HandleDecision(decision, log); });
            SetRunning(true);
            status.Text = "Connecting to AirPods control service…";
            worker = Task.Run(async () =>
            {
                string ended = "Control session ended. Refresh devices and reconnect.";
                try { await active.RunAsync(cancel.Token); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { ended = "Stopped. Reconnect explicitly when ready."; }
                catch (ConnectionInterruptedException error) { ended = ConnectionHealth.Instruction(error.Reason); log.Write("recovery", "interrupted", error.Reason.ToString()); }
                catch (Exception error)
                {
                    log.Write("tray", "session_failed", error.GetType().Name);
                    ended = "Control connection failed. Check Bluetooth, the selected output and transport support, then refresh devices and reconnect.";
                }
                finally
                {
                    await writer.DisposeAsync();
                    Dispatch(async () =>
                    {
                        if (!ReferenceEquals(controller, active)) return;
                        ClearOffer(); await ClearNotificationsAsync();
                        if (!ReferenceEquals(controller, active)) return;
                        running = null; worker = null; controller = null; cancel.Dispose();
                        inventory = null; SetRunning(false); status.Text = ended;
                    });
                }
            });
        }
        catch { status.Text = "Could not start or save settings. Check the diagnostics folder's write permissions, then refresh devices."; }
        finally { lifecycle.Release(); }
    }
    private void SetRunning(bool value)
    {
        refresh.Enabled = radios.Enabled = devices.Enabled = endpoints.Enabled = transport.Enabled =
            confirmedOutput.Enabled = hostName.Enabled = protocolDebug.Enabled = !value;
        stop.Enabled = value;
        UpdateSetup();
    }
    private void UpdateStatus(AirPodsState state)
    {
        if (!state.ControlConnected) { ClearOffer(); status.Text = "Control session closed. Refresh devices and reconnect."; return; }
        string model = state.Identity?.Value.Model?.Name ?? "AirPods";
        string source = state.Source is null ? "Unknown" : state.Source.Value.Type == AudioSourceType.None ? "Idle" :
            state.Source.Value.Address.ToString() == settings?.RadioAddress ? "This PC" : "Another device";
        status.Text = $"{model} • {source} • {state.Source?.Value.Type.ToString() ?? "Unknown"} • {state.Devices?.Value.Length.ToString() ?? "?"} connected hosts";
    }
    private void HandleDecision(SwitchDecision decision, EventLog log)
    {
        if (decision.Action == SwitchAction.Offer && decision.RequestId is { } id)
        {
            offeredId = id; switchNow.Enabled = reject.Enabled = true;
            if (notify.Checked && notifications is not null)
            {
                try { notifications.Offer(id); }
                catch (Exception error) { log.Write("notification", "failed", error.GetType().Name); status.Text = "Notification unavailable. Use Switch here."; }
            }
        }
        if (decision.Action is SwitchAction.Cancel or SwitchAction.Confirmed or SwitchAction.Failed or SwitchAction.SendTakeover)
        { ClearOffer(); _ = ClearNotificationsAsync(); }
        if (decision.Action is SwitchAction.Confirmed or SwitchAction.Failed)
            status.Text = decision.Action == SwitchAction.Confirmed ? "Windows source and render confirmed. Verify audible playback." : "Switch not confirmed. No automatic retry.";
        else if (decision.Action == SwitchAction.None && offeredId is null)
            status.Text = decision.Reason.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                decision.Reason.Contains("fresh", StringComparison.OrdinalIgnoreCase)
                ? "Waiting for fresh AirPods state. Safe refresh commands are not established for this alpha."
                : "Connected. " + decision.Reason;
    }
    private void ClearOffer() { offeredId = null; switchNow.Enabled = reject.Enabled = false; }
    private async Task ClearNotificationsAsync()
    {
        try { if (notifications is not null) await notifications.ClearAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { /* Old notification actions cannot match a new controller's request IDs. */ }
    }
    private void Respond(bool accept)
    {
        if (!suspended && offeredId is { } id) controller?.Post(new(id, accept));
    }
    private async Task StopAsync(string message)
    {
        await lifecycle.WaitAsync();
        try
        {
            setupGeneration++;
            var active = controller;
            running?.Cancel(); ClearOffer();
            if (worker is { } task) await task;
            // Worker cleanup can already have run on the UI thread during the await.
            if (ReferenceEquals(controller, active))
            {
                running?.Dispose(); running = null; worker = null; controller = null;
            }
            await ClearNotificationsAsync();
            inventory = null; confirmedOutput.Checked = false; SetRunning(false); status.Text = message;
        }
        finally { lifecycle.Release(); }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        // Cancel immediately, even if the UI thread is still awaiting a device refresh.
        if (args.Mode == PowerModes.Suspend)
        {
            try { running?.Cancel(); }
            catch (ObjectDisposedException) { /* Session cleanup won the race with this system callback. */ }
        }
        Dispatch(async () =>
        {
            if (args.Mode == PowerModes.Suspend)
            { suspended = true; await StopAsync(ConnectionHealth.Instruction(ConnectionLoss.Suspended)); }
            else if (args.Mode == PowerModes.Resume)
            { suspended = false; await StopAsync("Windows resumed. Refresh devices, confirm the output, and reconnect."); }
        });
    }
    private async Task ExportAsync()
    {
        using var dialog = new SaveFileDialog { Title = "Export redacted diagnostics", Filter = "ZIP archive|*.zip",
            FileName = "AirPodsSwitch-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(dialog.FileName)) { status.Text = "Choose a new filename. Diagnostics exports never overwrite existing files."; return; }
        export.Enabled = false;
        var snapshot = inventory;
        var summary = new DiagnosticsSummary(AppVersion, Environment.OSVersion.Version.ToString(), SelectedTransport,
            SelectedRadio is not null, SelectedDevice is not null, SelectedOutput is not null, notify.Checked,
            snapshot?.Radios.Count, snapshot?.Radios.Sum(r => r.Devices.Count), snapshot?.Outputs.Count,
            snapshot?.DriverInterfaces.Count, snapshot is not null, running is not null);
        try
        {
            Directory.CreateDirectory(TraySettings.DirectoryPath);
            string[] files = DiagnosticsBundle.FindRecentLogs(TraySettings.DirectoryPath);
            await Task.Run(() => DiagnosticsBundle.Export(dialog.FileName, summary, files));
            status.Text = "Diagnostics exported. The ZIP contains redacted metadata and selection flags; no names, addresses, settings, backups or packet payloads.";
        }
        catch { status.Text = "Diagnostics export failed. Choose a new filename in a writable folder and try again."; }
        finally { export.Enabled = true; }
    }
    private async Task QuitAsync()
    {
        quitting = true;
        await StopAsync("Exiting…");
        try { notifications?.Dispose(); }
        catch { /* Registration teardown must not keep the application alive. */ }
        icon.Visible = false; Close();
    }
    private void Dispatch(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
    }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OpenSetupGuide()
    {
        string local = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "docs", "ALPHA_GUIDE.md"));
        string revision = AppVersion.Split('+').Last();
        if (revision.Length != 40 || !revision.All(Uri.IsHexDigit)) revision = "main";
        Open(File.Exists(local) ? local : $"https://github.com/hugo2006alm/airpods-switch/blob/{revision}/docs/ALPHA_GUIDE.md");
    }
    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { status.Text = "Could not open settings or the guide. Open Windows Bluetooth/Sound settings directly."; }
    }
}
