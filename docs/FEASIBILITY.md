# Technical assessment: 2026-10-15

This is the first substantial report. Exact audited commits and adaptation mapping are
in [UPSTREAM_MAP.md](UPSTREAM_MAP.md). No physical AirPods/Android switching test has passed.

## Findings and answers

1. **AirPods 5 support — VERIFIED IN SOURCE.** Current LibrePods has separate AirPods 5
   and wireless-case model classes with six model identifiers, shared AACP handlers and
   capability differences. BLE name table lacks their model IDs. See [AIRPODS5](AIRPODS5.md).
2. **Reverse-engineered switching — VERIFIED IN SOURCE.** Ownership control, source
   notifications, host lists, targeted routing relay, Hijackv2 and ReverseBannerTapped
   are implemented. Exact request bodies and some routing metadata remain undocumented.
   A successful write is not an observed ownership transition. See [AACP_SWITCHING](AACP_SWITCHING.md).
3. **Unrooted Android observation — conditional.** Battery/wear/listening/source/list
   parsers are not vendor-hook gated, but AACP needs an OS-compatible L2CAP connection.
   BLE uses ordinary scanning; fine-grained fields may need previously obtained keys.
   Nothing OS beta compatibility has not been established.
4. **Vendor spoof gate — VERIFIED IN SOURCE.** Normal connected `takeOver()` returns
   when hook disabled or ownership cache is zero. Reverse has an earlier special branch.
   Callbacks are not universally gated. Local MAC and profile connect/disconnect add
   separate permission/hidden-API issues. See [ANDROID_LIMITATIONS](ANDROID_LIMITATIONS.md).
5. **README multipoint claim — only partly substantiated.** Real relay, ownership and
   other-device callbacks exist. README marks multipoint as vendor-spoof dependent.
   Source does not prove two-host routing with exactly one Apple-identified host, nor
   this model/firmware, phone build or Windows adapter.
6. **Tblob18 Windows port — research, not a working handoff foundation.** Latest audited
   commit is 2025-11-21 versus primary upstream 2026-10-15, roughly eleven months older.
   This measures tip dates, not an exact upstream commit distance. It contains a Linux
   Qt port with Windows Core Audio enumeration/volume and sleep handling, basic protocol
   packets, BLE and cross-device socket relay scaffolding. `PlayerStatusWatcher` is an
   explicit no-op, pause/play/media enumeration are unfinished, `setCardProfile` returns
   true from a placeholder, and `main.cpp` requests unsupported Windows Qt L2CAP sockets.
   No current active-source, AACP connected-host-list or ownership/smart-routing state
   machine was found in Windows runtime code. Qt `connectedDevices()` is local host
   connection state, not the AirPods host list. Android/Windows handoff marketing is not
   supported by a complete live implementation.
7. **Required Windows APIs — VERIFIED BY OFFICIAL DOCUMENTATION.** Win32 Bluetooth
   enumerates radios/devices; WinRT covers BLE/GSMTC; Core Audio covers endpoints/sessions;
   App SDK supports actionable notifications. Classic AACP requires a profile-driver
   L2CAP channel, not stock user-mode RFCOMM. See references below.
8. **Windows DID — documented configuration, unproven effect on buds.** BTHPORT DID
   DWORDs are officially documented. Global path scope is known; actual publication,
   cache/reload, USB/integrated equivalence and mixed-host acceptance need testing.
   Use reversible tools and external SDP observation. See [WINDOWS_DID](WINDOWS_DID.md).
9. **Without hardware:** build bounded packet parsing/serialization, source/store rules,
   call protection/cooldown/timeout simulations, Windows diagnostics, reversible registry
   logic with fake-store tests, media monitor, driver adapter and tray integration.
10. **Hardware gate:** identify physical model/firmware, confirm phone AACP without root,
    driver transport, two-host persistence, emitted source/list/ownership, accepted relay,
    reverse behavior and actual audio. Repeat with USB desktop and integrated laptop.
11. **Recommended stack:** .NET 10 C# with isolated Windows APIs/driver boundary.
    C++/WinRT and Qt do not remove the L2CAP driver requirement. See [ADR-001](ADR-001-application-architecture.md).
12. **Repository:** independent GPL-3.0-or-later modules, separate ignored read-only clones,
    selective ports with exact file/SHA mapping. No fork strategy change justified.
13. **Critical risks:** ordinary Android cannot guarantee profile release; beta OS may
    fail AACP; firmware may require both hosts' DID/capabilities; signed compatible driver
    distribution; unknown AirPods 5 BLE IDs; MAC order/rotating identity; stale/conflicting
    source; noisy media detection; App SDK activation lifecycle; false route confirmation.
14. **Shortest genuine takeover proof:** prepare diagnostic tool; verify installed driver
    SDUs and model; record baseline without DID changes; verify phone AACP; obtain external
    Windows DID evidence; establish two host list/source; run one user-confirmed ownership
    + Hijackv2 experiment; require post-request Windows MEDIA source and correlated render
    activity, plus listening confirmation. Capture both logs; stop after timeout, no loop.
    Failure of the minimum relay does not establish impossibility; investigate missing
    metadata/init before choosing the Android bridge.

## Windows reuse audit

| Component | Existing implementation | Reuse | Port upstream | Rewrite |
|---|---|---:|---:|---:|
| Basic AACP layouts | Byte constants, battery/ear/ANC parsing | Small layout facts only | Current Android | Bounds-safe C# parser |
| AirPods model registry | Old generations | No | Current AirPods5 classes | Unknown-safe lookup |
| Bluetooth transport | Qt L2CAP request on Windows | No | Not from Android private constructors | Installed profile-driver boundary |
| BLE | Qt scan/crypto integration | Research only | After model evidence | Isolated WinRT diagnostic |
| Source / connected hosts / ownership | Not found in Windows runtime | No | Current Android layouts | Store and transition rules |
| Windows media | Stub / unfinished hooks | No | No | GSMTC plus Core Audio evidence |
| Windows audio | Endpoint enumeration / volume | Concepts only | No | Supported observer, no default-route hack |
| Cross-device relay | Phone socket / notification scaffold | No | No | Deferred authenticated bridge only after failed experiment |
| Tray / power / autostart | Windows Qt adapters | No | No | Minimal managed tray; autostart opt-in/deferred |
| DID diagnostics | Not found | No | No | Exact reversible original implementation |

## Official API evidence

- [Windows sockets](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-socket):
  AF_BTH stream sockets support RFCOMM. AACP cannot be made RFCOMM by passing its UUID.
- [Bluetooth driver stack](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-driver-stack):
  BTHPORT contains L2CAP/SDP; profile drivers use Bluetooth request blocks.
- [L2CAP client](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/creating-a-l2cap-client-connection-to-a-remote-device):
  kernel profile-driver open/close and negotiated MTU are the supported lower-level path.
- [SDP](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/communicating-with-sdp-servers):
  service publication/query is distinct from changing DID and from audio routing.
- [GSMTC](https://learn.microsoft.com/en-us/uwp/api/windows.media.control?view=winrt-26100):
  exposes apps participating in SMTC; it is not an exhaustive call/game audio classifier.
- [Core Audio session control](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessioncontrol2):
  process/session and system-sound identity support filtering; release COM on its owner thread.
- [Notifications](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/):
  Windows App SDK supports local notifications for packaged and unpackaged desktop apps.
- Qt's pinned `qbluetoothsocket_winrt.cpp` explicitly rejects non-RFCOMM protocols.

**PLAUSIBLE / REQUIRES TESTING:** a compatible installed profile driver (MagicAAP-style
device interface) can carry this app's SDUs. Supplemental bridge source demonstrates
that ABI, but this app does not bundle its test-signed driver, import certificates,
change Secure Boot/test signing, or certify those builds. No source claim substitutes
for testing the actual installed driver and hardware.
