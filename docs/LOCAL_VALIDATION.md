# Local validation and hardware gates

2026-10-15: .NET SDK 10.0.402 installed in ignored `.tools/dotnet` for this checkout.
Core protocol/policy/DID tests run without hardware. The full Windows solution builds
with warnings treated as errors. See later validation commits for final test totals.

**VERIFIED LOCALLY:** Windows build 26200 (25H2), non-administrator inventory enumerated
one Classic Bluetooth radio. No paired AirPods were listed. Addresses and personal
device names are omitted from this report.

**VERIFIED LOCALLY:** both RFCOMM (3) and L2CAP (256) socket creation succeeded.
`SO_PROTOCOL_INFOW` reports distinct `MSAFD RfComm [Bluetooth]` and
`MSAFD L2CAP [Bluetooth]` providers with the corresponding protocol numbers. This
does **not** establish successful bind/connect to PSM 0x1001, encryption, receive SDU
boundaries or AACP exchange. A provider catalog entry is weaker evidence than those.

The SDK `ws2bth.h` defines BTHPROTO_L2CAP, SOL_L2CAP, packed 30-byte SOCKADDR_BTH, and
describes port as RFCOMM channel or L2CAP PSM. Microsoft's public socket article still
documents RFCOMM as supported. The default live path remains the compatible installed
profile-driver interface. `--transport winsock-experimental` exists solely to measure
the fuller direct path on actual paired hardware; failure must be retained with stage
and Win32/socket error. No provider-creation result is a multipoint claim.

Physical gates remain: real model/firmware, phone OS support, L2CAP exchange, two-host
state, DID publication/acceptance, ownership/source transition, and audible playback.
Do not promote the technical MVP to hardware-validated until the experiment passes.

Follow-up **VERIFIED LOCALLY**: explicit L2CAP local bind on the enumerated radio
returned Winsock **10050 (NetworkDown)** while WinRT reported that radio **On**. No
remote connection was attempted. Record this provider/adapter limitation, not an AirPods
or mixed-DID result. Default profile-driver interface list was empty. A future direct
test must retain bind-stage errors; changing DID is not a transport fix established here.

Final local software validation: **52 deterministic tests passed**, whole solution built
with zero warnings/errors, and Windows x64 diagnostic/tray programs published locally.
Published CLI inventory, BLE scan startup/cancellation, media debounce, synthetic replay,
and DID status/backup were exercised. No system DID writes occurred. A native notification
object was constructed successfully by the published tray's headless self-test, without
registering or showing a notification. Visual tray behavior and action delivery remain
unverified. No source/ownership or audible playback result was observed from physical buds.

Initial GitHub CI workflow creation was rejected for missing OAuth `workflow` scope.
The workflow was initially stored as a documentation example. The unpushed workflow
commit was replaced locally; published remote history was not rewritten.

Follow-up: GitHub CLI reauthentication granted `workflow` scope. The reviewed workflow
is now installed at `.github/workflows/build.yml`. Its exact restore/build/test/replay
commands passed locally: 52 tests, zero build warnings/errors, and no rejected synthetic
packets. Hosted run results are recorded by GitHub Actions separately from local evidence.

**VERIFIED ON GITHUB ACTIONS:** [initial hosted run](https://github.com/hugo2006alm/airpods-switch/actions/runs/38813371292)
built with zero warnings/errors, passed all 52 tests, replayed fixtures with zero rejected
packets, and uploaded TRX results. The Windows job took 1 min 44 s. The repository is now
public at the owner's request, using a standard hosted runner. The run flagged the old
artifact action's Node 20 runtime; the workflow now pins upload-artifact v7.0.2 (Node 24).
