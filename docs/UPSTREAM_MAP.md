# Upstream tracking

Audit date: 2026-10-15. The repository remains standalone. Research directories are
ignored independent clones; their push URL is `https://invalid.local/READ_ONLY`.
Never commit research trees, Apple assets, keys, binary drivers, or upstream history.

| Reference | Exact commit | Scope |
|---|---|---|
| [LibrePods](https://github.com/librepods-org/librepods/tree/4589176f7b7943a6091d5c6907c74f441bcf7780) | `4589176f7b7943a6091d5c6907c74f441bcf7780` | Android protocol, model registry, BLE, service, Linux battery layout; GPL-3.0-or-later source notices |
| [Tblob18 Windows experiment](https://github.com/Tblob18/librepods-windows/tree/a439cadb517f6973b5d57dd2e0d3fbfdde16fe97) | `a439cadb517f6973b5d57dd2e0d3fbfdde16fe97` | Critical audit only; no application code copied |
| [Qt Windows socket implementation](https://github.com/qt/qtconnectivity/blob/faf5705d04c27707071b6ac029d738608506d2f4/src/bluetooth/qbluetoothsocket_winrt.cpp) | `faf5705d04c27707071b6ac029d738608506d2f4` | Verified backend accepts RFCOMM only; research only |
| [Windows bridge transport](https://github.com/will-ch-h/librepods-windowsbridge/tree/fbabc879aac40267d6e806470933f6be3695fdd6) | `fbabc879aac40267d6e806470933f6be3695fdd6` | `windows/winl2capsocket.{cpp,h}`: SetupAPI GUID and one-SDU-per-read ABI; selectively adapted with GPL attribution |
| [WinPods](https://github.com/changcheng967/WinPods/tree/951ceb076dca2420c8c33fe47b97dc19fc7f9232) | `951ceb076dca2420c8c33fe47b97dc19fc7f9232` | Supplemental driver architecture research only; no license found in audited tree, no code copied |

## Selective adaptation map

All LibrePods paths below refer to the exact commit above. Source code being present
does not demonstrate this product's hardware compatibility.

| Local component | Upstream file / symbols | Adaptation |
|---|---|---|
| `src/AirPodsSwitch.Core/Protocol/AacpProtocol.cs` | `android/app/src/main/java/me/kavishdevar/librepods/bluetooth/AACPManager.kt`: opcodes, handshake, control, `parseAudioSourceResponse`, `parseConnectedDevicesResponse`, `createHijackRequestPacket` | C# translation of required layouts; complete bounds checks; both opcode bytes; reject partial lists; preserve SDU boundaries; no secret extraction |
| `src/AirPodsSwitch.Core/Protocol/AirPodsModels.cs` | `android/app/src/main/java/me/kavishdevar/librepods/data/AirPods.kt`: `AirPods5`, `AirPods5WCC` | Model IDs/capability data only; unknown models remain unknown |
| `src/AirPodsSwitch.Core/Protocol/AacpProtocol.cs` battery/ear parsing | `android/.../data/Packets.kt`, `linux/battery.hpp` | Variable component count, explicit unknown/disconnected battery state; no stale retained percentage |
| `src/AirPodsSwitch.Windows/Bluetooth/ProfileDriverTransport.cs` | windows bridge `windows/winl2capsocket.cpp` | Rewritten managed overlapped I/O, cancellation, bounded operations; explicit device selection and availability; no driver bundled/installed |
| Policy, logging, DID, media, tray, tests | Original implementation | No upstream UI or reconnect logic reused |

## Synchronization procedure

Fetch research remotes only on an intentional audit; record the next exact SHA in a
reviewable commit. Diff mapped symbols, compare packet fixtures, run parser/policy/DID
tests, and repeat affected physical experiments. Never replace files wholesale from
moving `main`. Update this table whenever a component is adapted.
