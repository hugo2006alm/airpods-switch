// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using AirPodsSwitch.Core.Diagnostics;
using AirPodsSwitch.Core.Did;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;
using AirPodsSwitch.Windows.Bluetooth;
using AirPodsSwitch.Windows.Did;
using AirPodsSwitch.Windows.Media;
using AirPodsSwitch.Windows.Switching;

return await DiagnosticProgram.RunAsync(args);

internal static class DiagnosticProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        try
        {
            var command = args.FirstOrDefault() ?? "--help";
            var options = Options(args.Skip(command == "did" ? 2 : 1).ToArray());
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            if (options.TryGetValue("seconds", out var seconds))
            {
                if (!int.TryParse(seconds, out int duration) || duration is < 1 or > 3600)
                    throw new ArgumentException("--seconds must be 1..3600");
                cancel.CancelAfter(TimeSpan.FromSeconds(duration));
            }
            switch (command)
            {
                case "help": case "--help":
                    Console.WriteLine("""
                        airpods-diag inventory
                        airpods-diag transport-probe [--radio <local MAC>]
                        airpods-diag ble [--seconds 15]
                        airpods-diag audio
                        airpods-diag support --file <new-diagnostics.zip>
                        airpods-diag media [--seconds 15]
                        airpods-diag did status|verify
                        airpods-diag did backup --file <new.backup.json>
                        airpods-diag did apply-apple --file <backup> --confirm APPLY-APPLE
                        airpods-diag did restore --file <backup> --confirm RESTORE-DID
                        airpods-diag replay --file <one-SDU-per-line.hex> [--include-addresses]
                        airpods-diag watch --device <MAC> --radio <MAC> [--seconds 60] [--protocol-debug]
                        airpods-diag takeover --device <MAC> --radio <MAC> --endpoint-id <ID> [--confirm SWITCH]
                        Live commands optionally accept --transport winsock-experimental.
                        Default live transport: installed profile-driver. No driver installation or automatic DID changes.
                        Diagnostic inventory/audio output includes device identifiers. Source MACs redacted by default.
                        """);
                    return 0;
                case "inventory":
                    Print(new
                    {
                        evidence = "VERIFIED LOCALLY: cached inventory; AirPods routing not tested",
                        windows = Environment.OSVersion.VersionString,
                        administrator = WindowsDidRegistryStore.IsAdministrator(),
                        radios = BluetoothInventory.Read(),
                        adapterIdentifiers = await AdapterInventory.ReadAsync(cancel.Token),
                        profileDriverInterfaces = ProfileDriverTransport.InterfacePaths(),
                        aacp = "SDP service/interface presence is not a live link; use explicit watch experiment"
                    });
                    return 0;
                case "transport-probe":
                    Print(SocketProbe.Run(options.TryGetValue("radio", out var localRadio) ? BluetoothAddress.Parse(localRadio) : null));
                    return 0;
                case "ble":
                    if (!options.ContainsKey("seconds")) cancel.CancelAfter(TimeSpan.FromSeconds(15));
                    await BleDiagnostic.WatchAsync(new EventLog(Console.Out), cancel.Token);
                    return 0;
                case "audio": Print(await Task.Run(WindowsAudioManager.Read)); return 0;
                case "support":
                    SetupInventory? supportInventory;
                    try { supportInventory = await SetupInventory.ReadAsync(cancel.Token); }
                    catch (Exception) when (!cancel.IsCancellationRequested) { supportInventory = null; }
                    var supportSummary = new DiagnosticsSummary(
                        typeof(DiagnosticProgram).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
                        Environment.OSVersion.Version.ToString(), "unknown", false, false, false, false,
                        supportInventory?.Radios.Count, supportInventory?.Radios.Sum(r => r.Devices.Count),
                        supportInventory?.Outputs.Count, supportInventory?.DriverInterfaces.Count,
                        supportInventory is not null, false);
                    var supportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPodsSwitch");
                    DiagnosticsBundle.Export(Require(options, "file"), supportSummary, DiagnosticsBundle.FindRecentLogs(supportFolder));
                    Print(new { result = "Local redacted support ZIP created; no raw logs, addresses, names, settings, backups or keys" });
                    return 0;
                case "did": return Did(args.ElementAtOrDefault(1) ?? "status", options);
                case "replay": return await ReplayAsync(Require(options, "file"), options.ContainsKey("include-addresses"), cancel.Token);
                case "media":
                    if (!options.ContainsKey("seconds")) cancel.CancelAfter(TimeSpan.FromSeconds(15));
                    var monitor = new WindowsMediaMonitor();
                    using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500)))
                    {
                        while (await timer.WaitForNextTickAsync(cancel.Token))
                        {
                            var sample = await monitor.SampleAsync(options.GetValueOrDefault("endpoint-id"), cancel.Token);
                            Print(new
                            {
                                timestamp = DateTimeOffset.UtcNow, sample.AppId, sample.Playback,
                                sample.State.Meaningful, sample.State.CallOrCaptureActive,
                                routeCorrelated = sample.Render.CorrelatedAirPodsRender,
                                evidence = "Windows media observation only; no titles/history or routing changes"
                            });
                        }
                    }
                    return 0;
                case "watch": case "takeover":
                    return await LiveAsync(command == "takeover", options, cancel);
                default: throw new ArgumentException("Unknown command; use --help");
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            new EventLog(Console.Error).Write("diagnostic", "error", $"{error.GetType().Name}; code=0x{error.HResult:X8}; {error.Message}");
            return 1;
        }
    }

    private static int Did(string command, Dictionary<string, string> options)
    {
        var registry = new WindowsDidRegistryStore();
        var manager = new DidManager(registry, WindowsDidRegistryStore.MachineFingerprint(), WindowsDidRegistryStore.IsAdministrator);
        switch (command)
        {
            case "status": case "verify":
                Print(new
                {
                    windows = Environment.OSVersion.VersionString,
                    administrator = WindowsDidRegistryStore.IsAdministrator(), radios = BluetoothInventory.Read(),
                    configuration = manager.Status(), bthport = registry.RelevantConfiguration(),
                    valueSummary = manager.Status().Values.ToDictionary(p => p.Key, p => new
                    {
                        p.Value.Present, p.Value.Kind,
                        dword = p.Value is { Present: true, Kind: 4, Data.Length: 4 } ? BitConverter.ToUInt32(p.Value.Data) : (uint?)null
                    }),
                    verification = "CONFIGURATION ONLY; external SDP not observed; remote AirPods identity acceptance not confirmed",
                    mutations = "none"
                }); return 0;
            case "backup":
                DidManager.SaveNew(Require(options, "file"), manager.Capture());
                Print(new { result = "Backup created exclusively; original presence/type/raw bytes preserved" }); return 0;
            case "apply-apple": case "restore":
                var backup = DidManager.Load(Require(options, "file"));
                manager.Validate(backup);
                Print(new
                {
                    scope = @"HKLM\" + DidManager.RegistryPath,
                    operation = command,
                    exactModifications = command == "apply-apple"
                        ? "Only DIDVendorIDSource=DWORD 1 and DIDVendorID=DWORD 76 (0x004C); product/version unchanged"
                        : "Restore four DID values to original raw bytes/type/presence; absent values deleted",
                    noRestart = true
                });
                if (command == "apply-apple") manager.ApplyApple(backup, options.GetValueOrDefault("confirm", ""));
                else manager.Restore(backup, options.GetValueOrDefault("confirm", ""));
                Print(new { result = "Configuration readback matched; actual SDP and AirPods acceptance remain unverified" }); return 0;
            default: throw new ArgumentException("Unknown did command");
        }
    }

    private static async Task<int> LiveAsync(bool takeover, Dictionary<string, string> options, CancellationTokenSource cancel)
    {
        if (!options.ContainsKey("seconds")) cancel.CancelAfter(TimeSpan.FromSeconds(60));
        var log = new EventLog(Console.Out, options.ContainsKey("protocol-debug"));
        var live = new LiveController(new(BluetoothAddress.Parse(Require(options, "device")),
            BluetoothAddress.Parse(Require(options, "radio")), options.GetValueOrDefault("endpoint-id"),
            options.GetValueOrDefault("transport", "profile-driver"), takeover), log);
        live.StateChanged += state => Print(StateReport(state, log, options.ContainsKey("include-addresses")));
        int result = takeover ? 4 : 0;
        live.Decision += decision =>
        {
            if (decision.Action == SwitchAction.Offer && decision.RequestId is { } id)
            {
                if (options.GetValueOrDefault("confirm") == "SWITCH") live.Post(new(id, true));
                else
                {
                    Console.Error.WriteLine("Experimental takeover. Calls protected. Type SWITCH to send one minimum ownership/Hijackv2 sequence, or Enter to reject:");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            string? answer = await Console.In.ReadLineAsync(cancel.Token);
                            live.Post(new(id, answer == "SWITCH"));
                        }
                        catch (OperationCanceledException) { }
                    });
                }
            }
            if (decision.Action is SwitchAction.Confirmed or SwitchAction.Failed)
            {
                result = decision.Action == SwitchAction.Confirmed ? 0 : 4;
                cancel.Cancel();
            }
        };
        try { await live.RunAsync(cancel.Token); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        if (takeover) Print(new
        {
            result = result == 0 ? "Protocol source and correlated render evidence confirmed; verify audible playback" :
                "TAKEOVER NOT CONFIRMED; no eligible offer, failure or timeout; no retry loop",
            physicalListeningConfirmation = "NOT RECORDED"
        });
        return result;
    }

    private static async Task<int> ReplayAsync(string path, bool includeAddresses, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new ArgumentException("Replay exceeds 16 MiB limit");
        var store = new AirPodsStateStore();
        long epoch = store.BeginConnection();
        var log = new EventLog(Console.Out);
        int rejected = 0;
        using var file = new StreamReader(path);
        while (await file.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > AacpProtocol.MaxPacketSize * 3) throw new ArgumentException("Oversized replay line");
            line = line.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var parsed = AacpProtocol.Parse(Convert.FromHexString(string.Concat(line.Where(c => !char.IsWhiteSpace(c)))));
            if (!parsed.Success) { log.Write("replay", "rejected", parsed.Error!); rejected++; continue; }
            if (store.Apply(parsed.Event!, MonotonicClock.Milliseconds, epoch)) Print(StateReport(store.Current, log, includeAddresses, simulation: true));
        }
        log.Write("replay", "complete", $"SIMULATED packet input; rejected={rejected}; no hardware or transport");
        return rejected == 0 ? 0 : 3;
    }
    private static object StateReport(AirPodsState state, EventLog log, bool includeAddresses, bool simulation = false) => new
    {
        evidence = simulation ? "VERIFIED LOCALLY: synthetic parser replay; no hardware" : "PLAUSIBLE / REQUIRES TESTING: live protocol interpretation",
        timestamp = DateTimeOffset.UtcNow, state.Epoch, state.ControlConnected,
        model = state.Identity?.Value.Model?.Name ?? "Unknown", modelNumber = state.Identity?.Value.ModelNumber,
        firmware = state.Identity?.Value.Firmware, wear = state.Wear?.Value, batteries = state.Batteries?.Value,
        connectedHostCount = state.Devices?.Value.Length,
        connectedHosts = state.Devices?.Value.Select(d => includeAddresses ? d.Address.ToString() : log.PrivateId(d.Address.ToString())),
        source = state.Source is { } source ? includeAddresses ? source.Value.Address.ToString() : log.PrivateId(source.Value.Address.ToString()) : null,
        sourceType = state.Source?.Value.Type.ToString() ?? "UNKNOWN", ownsConnection = state.OwnsConnection?.Value
    };
    private static Dictionary<string, string> Options(string[] args)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal) { "protocol-debug", "include-addresses" };
        var values = new HashSet<string>(StringComparer.Ordinal) { "seconds", "file", "confirm", "device", "radio", "endpoint-id", "transport" };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected --option");
            string name = args[i][2..];
            if (flags.Contains(name)) result.Add(name, "true");
            else if (values.Contains(name) && i + 1 < args.Length) result.Add(name, args[++i]);
            else throw new ArgumentException($"Unknown option or missing value: {name}");
        }
        return result;
    }
    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"--{name} is required");
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonSettings.Options));
}
