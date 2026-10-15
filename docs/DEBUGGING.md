# Diagnostics and captures

Use JSONL diagnostics first. `inventory`, `audio`, `did status`, `transport-probe`,
`watch`, `takeover`, `media`, `replay`, and `ble` are separate commands. `--protocol-debug`
must be explicit and adds opcode/length metadata only. No packet payload, link key,
BLE encryption/IRK key, authentication secret or media title is logged.

Normal live logs pseudonymize host MACs with a random per-run HMAC salt. Source and host
list identity can be compared within one run, not across runs. Raw diagnostic inventory
contains addresses/device IDs; keep those files private. Tray logs use three circular
10 MiB segments per connection in `%LOCALAPPDATA%\AirPodsSwitch`; settings are separate.
Recorded source and endpoint are distinct evidence, not interchangeable ownership flags.

## Windows paths

**VERIFIED BY OFFICIAL DOCUMENTATION:** Microsoft provides
[Bluetooth WPR instructions and a recording profile](https://github.com/microsoft/busiotools/blob/master/bluetooth/tracing/readme.md).
Provider availability depends on Windows/driver version. Inspect provider names locally
instead of hardcoding an unverified ETW GUID or WPR profile:

```powershell
logman query providers | Select-String -Pattern 'Bluetooth|BTH'
wpr -profiles
wevtutil el | Select-String -Pattern 'Bluetooth|BTH'
```

Run those read-only inventories first. Event Viewer Operational/System logs can identify
transport/radio resets; they do not necessarily include AACP payload. WPR profiles must
actually exist on the machine. Record tool versions, exact chosen provider/profile and
start/stop commands when a capture is requested. The project does not enable all ETW
providers or restart the Bluetooth service automatically.

If metadata is insufficient, review/download Microsoft's `BluetoothStack.wprp` from
that repository. In an elevated terminal, `wpr -start BluetoothStack.wprp!BluetoothStack -filemode`
starts the profile; reproduce once, then `wpr -stop BthTracing.etl`. This is an explicit
user-run capture, not automatic telemetry. Check no unrelated WPR session will be
interrupted. The Light profile reduces verbosity. No boot autologger is installed.

HCI payload visibility depends on adapter/driver and capture method. Installing Wireshark
or Npcap alone does not prove Windows Bluetooth HCI capture is available. Raw captures
are justified only when existing metadata cannot explain PSM negotiation, missing source
queries, SDU boundaries or routing relay acceptance. They can contain Bluetooth secrets
and unrelated device traffic: do not include them in normal logs or commit them. Extract
the minimum sanitized protocol fixture separately after review.

## Android paths

Use LibrePods built-in diagnostic/log collection first. Record whether AACP itself is
connected, source/type/list events, vendor hook disabled, and model/firmware. Upstream
debug output can include proximity keys/serials; review/redact before sharing.

If USB debugging is already enabled and the user approves connecting ADB, capture
application-specific logs rather than all system logs:

```powershell
adb devices
adb shell getprop ro.build.version.release
adb shell getprop ro.build.display.id
adb shell pidof me.kavishdevar.librepods
# Replace PID from prior command:
adb logcat --pid=PID -v threadtime
```

USB debugging and Bluetooth HCI snoop are optional user-controlled developer settings,
not root. Do not assume Nothing OS beta exports HCI snoop in an accessible location;
test availability and privacy scope before asking for a bug report. Root-only capture,
hidden-API routing, stack-library replacement and firmware modification are outside
the intended production path.

## Fault isolation order

1. No radio / no pairing → normal Windows inventory/setup.
2. No driver interface / direct bind-connect failure → transport gate, no multipoint conclusion.
3. No handshake → channel security/framing/firmware initialization.
4. Model unknown → actual model-information fixture, no Pro 2 substitution.
5. No source/list → firmware/DID/notification or missing query investigation.
6. Call/capture/wear/staleness veto → inspect policy reason, not a send failure.
7. Commands sent, source unchanged → minimum relay/init compatibility experiment.
8. Source local, no render match → endpoint/AUMID correlation or Windows routing.
9. Source+render observed, no audible audio → hardware listening/route/codec problem.

No automatic retry loop or forced A2DP reconnect hides these failures.
