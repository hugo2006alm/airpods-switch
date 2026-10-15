# Build and command guide

Windows 11, x64, .NET SDK 10.0.402 (or a permitted 10.0.4xx patch). The Windows SDK
targeting pack comes from NuGet; Visual Studio and a WDK are not needed for this user-mode
application. No driver is built or installed. A local SDK was installed for this checkout
in `.tools/dotnet`; that directory is ignored and is not a project dependency.

```powershell
.\scripts\build.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"
.\scripts\publish.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"
```

On another PC with SDK installed globally, omit `-Dotnet`. Published self-contained
programs are `artifacts/diag/airpods-diag.exe` and `artifacts/tray/AirPodsSwitch.Tray.exe`.
Copy whole output directories including runtime/native DLLs and notices; do not copy
only the EXE. These local artifacts are not committed. Public binary distribution must
include corresponding GPL application source at the same revision.

```powershell
.\scripts\diag.ps1 --help
.\scripts\diag.ps1 inventory
.\scripts\diag.ps1 transport-probe
# Optional bind probe, no remote connection:
# .\scripts\diag.ps1 transport-probe --radio <this PC's Classic MAC>
.\scripts\diag.ps1 audio
.\scripts\diag.ps1 media --seconds 15
.\scripts\diag.ps1 ble --seconds 15
.\scripts\diag.ps1 replay --file tests/fixtures/two-host.hex
.\scripts\diag.ps1 did status
```

`inventory` and `audio` include identifiers and process/app IDs for diagnostics; never
media titles. BLE command reports Apple proximity frames without claiming identity,
AirPods 5 BLE model support, wear, or source. Replay uses explicitly synthetic data and
does not exercise Bluetooth. `media` requires SMTC participation and 2 s persistent
playback; Core Audio system sounds alone never create a meaningful session. Active
microphone capture or a known communication-app render session conservatively vetoes
takeover. Unknown AUMID/process correlation cannot confirm a render route.

Live AACP commands need explicit device/radio addresses and a functioning transport.
They do not auto-pair, auto-edit DID, auto-install drivers, or change the default audio
device. Run the [hardware procedure](HARDWARE_EXPERIMENT_01.md) before using takeover.
Source/list reads currently consume notifications; undocumented query payloads are not
fabricated. Minimum ownership/Hijackv2 is an experiment, not a complete proven protocol.

## Tray

Start `AirPodsSwitch.Tray.exe` normally, without elevation. Select radio, paired AirPods
and the matching AirPods output; choose transport, label this PC, then Connect. Settings
and a stable host ID are saved in `%LOCALAPPDATA%\AirPodsSwitch\settings.private.json`.
Use distinct labels for desktop/laptop; do not copy this private file between machines.
Closing the form hides it in the tray; Quit stops the session. Reconnection is explicit.
No autostart entry is installed. Native notifications require successful App SDK registration.
If unavailable, the status form exposes Switch/Not now for the same eligible request.
Stale notification actions cannot start a new takeover. No raw MAC appears in normal UI.

Diagnostic native startup test constructs a notification object without registering or
displaying one:

```powershell
.\artifacts\tray\AirPodsSwitch.Tray.exe --self-test "$PWD\.local\native-startup.json"
```

This is not a visual/activation/hardware test. Native action delivery, focus-assist policy,
tray lifecycle and actual source transition still need physical-machine testing.

## Testing and evidence

`dotnet test tests/AirPodsSwitch.Tests -c Release` covers packet truncation/fuzz inputs,
source/list MAC order, identity/model distinction, stale epochs, call/ear/freshness policy,
cooldown, duplicate/expired actions, conflicting source, send-only nonconfirmation,
timeouts, media debounce, exact registry kinds/absence, backup integrity, rollback, client
initialization/disposal and log secret suppression. Tests never edit the system registry.

Dependency versions/content hashes live in committed `packages.lock.json` files.
The CI template in `windows-build.workflow-example.yml` restores in locked mode and
runs the same Windows build/tests and synthetic replay. **Active CI is not installed:**
GitHub rejected the workflow push because the authenticated OAuth token lacks `workflow`
scope. Application/source/docs are pushed normally; local checks provide the current
validation. If the owner later enables that scope, copy the template to
`.github/workflows/build.yml` in a normal commit. CI cannot satisfy physical hardware gates.
