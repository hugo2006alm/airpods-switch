# Experiment 01: real two-host source takeover

Status: **PLAUSIBLE / REQUIRES TESTING**. Tooling is built; no physical switching result
is recorded. This test must run on the desktop and then laptop, using the actual AirPods
5 and unrooted Nothing Phone. Do not report a transport failure as proof that mixed-DID
multipoint is impossible. Separate the gates below.

## Prepare software and record identity

Use PowerShell from this checkout, or the self-contained CLI in `artifacts/diag`:

```powershell
# For this checkout the local SDK is already installed:
.\scripts\build.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"
.\scripts\publish.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"
$diag = "$PWD\artifacts\diag\airpods-diag.exe"
New-Item -ItemType Directory -Force -Path .local\experiment-01 | Out-Null
& $diag inventory > .local\experiment-01\inventory.private.json
& $diag transport-probe > .local\experiment-01\transport.json
& $diag audio > .local\experiment-01\audio.private.json
& $diag did status > .local\experiment-01\did-before.private.json
```

Record PC label (desktop/laptop), Windows build/UBR, adapter device ID and driver version
(Device Manager), Nothing OS full build/Android version and Play system-update date,
LibrePods version, actual AirPods model number and firmware. No root/Xposed changes.
Inventory should identify the intended radio and paired AirPods. A name match is only a
candidate; AACP must report one of the model IDs in [AIRPODS5.md](AIRPODS5.md).

If AirPods are missing, physically open the case and pair through Windows Settings.
The CLI does not pair automatically. Pair on the phone through normal Settings too.
Confirm ordinary playback independently on each host before attempting simultaneous use.

Set these task variables using exact values from inventory/audio, not example addresses:

```powershell
$buds = 'AA:BB:CC:DD:EE:FF' # REPLACE with paired AirPods Classic MAC
$radio = '11:22:33:44:55:66' # REPLACE with this PC's Classic radio MAC
$endpoint = '{0.0.0.00000000}.{REPLACE-WITH-AIRPODS-ENDPOINT-ID}'
```

Select the AirPods output in Windows Sound settings and confirm its identity by playing
brief audio. The endpoint mapping is an explicit experiment input, not automatically
trusted from a friendly name. Keep the phone's media titles/history out of returned logs.

## Gate A: working control channel, no DID changes

Default transport expects a compatible **already installed** AAP profile driver exposing
the interface GUID in [source map](UPSTREAM_MAP.md). Current development PC has none.
The application neither installs drivers nor changes test-signing/Secure Boot/certificates.
An arbitrary driver with a matching GUID is not certified by this project. Validate the
driver/publisher/signature/device binding independently if using this path.

The stock provider experiment can be tried first without installing anything:

```powershell
& $diag watch --device $buds --radio $radio --transport winsock-experimental --seconds 30 --protocol-debug > .local\experiment-01\direct.jsonl 2> .local\experiment-01\direct-error.jsonl
```

Record bind/connect/handshake errors exactly. Socket creation is known to work locally;
the development adapter's explicit local bind returned 10050 despite radio state On.
full channel access and SDU preservation are **unverified**. A bind/PSM error ends this
direct-path test. It does not establish phone/DID/multipoint behavior.

If a compatible validated profile driver is present, close other apps using its AACP
interface and run:

```powershell
& $diag watch --device $buds --radio $radio --seconds 30 --protocol-debug > .local\experiment-01\baseline.jsonl 2> .local\experiment-01\baseline-error.jsonl
```

Expected evidence, if supported: handshake acknowledged, model/firmware, battery,
wear changes when removing/reinserting each bud, ownership response, source and connected
host notifications. Change phone playback play/pause once while watching. Unknown fields
remain null/UNKNOWN. Record missing notifications, not inferred values. Do not request
or export BLE keys. Debug logs contain opcode/length metadata, never raw payloads.

Phone gate: open LibrePods without root. Confirm it actually connects AACP and shows
battery/ear updates. Record exact error if it cannot. BLE-only detection does not pass
this gate. Upstream does not certify Nothing OS beta. No Android bridge can grant the
missing Bluetooth privileges; do not prematurely implement one.

## Gate B: DID configuration and external publication

First retain baseline logs without modification. Follow [WINDOWS_DID.md](WINDOWS_DID.md)
for remote SDP observation. If you choose to run the Apple-DID experiment, an elevated
PowerShell must explicitly invoke:

```powershell
& $diag did backup --file .local\experiment-01\original.backup.json
& $diag did apply-apple --file .local\experiment-01\original.backup.json --confirm APPLY-APPLE
& $diag did verify > .local\experiment-01\did-after.private.json
```

Only two DWORDs change: source=1 and vendor=76. Product/version unchanged. Tool does not
restart Bluetooth. Record remote SDP 0x1200 before/after; if it remains unchanged, reboot
Windows deliberately and repeat. Distinguish configuration readback, remote SDP output,
and AirPods acceptance. No acceptance claim follows from `did verify` alone.

Keep the original backup for this PC. Restore with:

```powershell
& $diag did restore --file .local\experiment-01\original.backup.json --confirm RESTORE-DID
```

After restore/reboot, repeat remote SDP to verify publication returned. Do not reuse a
desktop backup on the laptop. Never send the backup to upstream or commit it.

## Gate C: simultaneous hosts and user-approved takeover

Wear both buds. Start Spotify on the phone and verify audible phone playback. Establish
Windows AACP using the validated transport; observe whether the phone stays connected.
In a second run, start the Windows experiment then start meaningful Windows playback
promptly (within the first few seconds of receiving source/list/wear events):

```powershell
& $diag takeover --device $buds --radio $radio --endpoint-id $endpoint --seconds 60 --protocol-debug > .local\experiment-01\takeover.jsonl 2> .local\experiment-01\takeover-prompts.txt
```

For direct transport add `--transport winsock-experimental`. Console prompt goes to the
stderr file; inspect it or omit that stderr redirect for an interactive prompt. Type
`SWITCH` in the console only when offered. Alternatively, adding `--confirm SWITCH`
explicitly approves this one diagnostic attempt in advance. It still requires fresh
eligible state, meaningful Windows media, worn buds, a known model and useful host list.
An observed call or active capture/communication session blocks the attempt.

Expected successful evidence:

1. At least two hosts; local radio in list, nonlocal MEDIA source identified.
2. One local takeover request ID and ownership/Hijackv2 send records.
3. Fresh post-request source MAC equals this Windows radio, type MEDIA.
4. Selected AirPods endpoint renders a matching Windows media process with nonzero meter.
5. Human confirms the Windows audio is audible and phone audio stopped/paused.
6. Phone LibrePods reports the new source/release/reverse event if it receives one;
   note whether AACP remains connected and whether Android requires manual route change.

CLI exit 0 for takeover means source+render conditions were observed, **not** that a human
confirmed sound. Exit 4 means not confirmed/no eligible attempt/timeout, exit 1 means
diagnostic/transport error. No send-only success and no automatic retry. Current minimum
experiment omits unverified metadata/init messages; failure warrants capture/research,
not immediate fallback selection.

Freshness limitation: source is valid for 5 seconds, wear/list for 15 seconds. The exact
source/list query bodies are not established by this audit, so the tool consumes
notifications only. If state expires before acceptance, no takeover is sent. Return that
result; discovering safe refresh queries is required for the eventual long-running UX.

## Gate D: policy, reverse and stability

- Phone CALL active → start Windows media: no automatic prompt or send.
- Both buds out → start Windows media: no send.
- Reject → same Windows session continues: no second prompt, including after cooldown.
- Force takeover timeout → one failure, retained source evidence, no retry/reconnect loop.
- Switch back using phone/LibrePods UI if available; observe source and reverse requests.
  No automatic undo command is implemented; this app does not fight the phone.
- Monitor for two minutes after a successful switch; record AACP/A2DP disconnects,
  audio interruptions and whether the phone retains its control link.
- Repeat USB desktop versus integrated laptop; avoid two Windows clients fighting for
  the same buds during this initial test. Label every PC/host distinctly.

## Return these results

Return redacted JSONL files plus the completed table. Keep raw addresses/adapter IDs and
DID backup private; `--include-addresses` is available only if a packet-identity problem
actually requires those addresses.

| Observation | Desktop | Laptop |
|---|---|---|
| Model / firmware / OS / driver | Pending | Pending |
| Phone AACP works unrooted | Pending | Same phone build |
| Transport: driver or direct; handshake; framing | Pending | Pending |
| External DID before/after/restore | Pending | Pending |
| Both hosts stay connected / AACP visible | Pending | Pending |
| Windows source/list/owns observed | Pending | Pending |
| Phone source/list/reverse observed | Pending | Pending |
| Request source changes to Windows MEDIA | Pending | Pending |
| Correlated AirPods render meter | Pending | Pending |
| Human audible Windows playback | Pending | Pending |
| Call protection / rejection / timeout / stability | Pending | Pending |

Only promote findings to **VERIFIED ON PHYSICAL HARDWARE** with the actual test files,
identity/version context and listening result. Phase 9 bridge work stays deferred.
