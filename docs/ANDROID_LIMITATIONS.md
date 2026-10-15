# Unrooted Nothing Phone limitations

Source evidence: LibrePods pinned commit in [UPSTREAM_MAP.md](UPSTREAM_MAP.md).
Current Nothing OS beta build, Play system update, LibrePods version, and AirPods firmware
must be recorded during the experiment. None has been inspected on the phone yet.

## Observation is conditional on transport

**VERIFIED IN SOURCE:** `BluetoothConnectionManager.kt` constructs Classic L2CAP sockets
using reflected private constructors, type 3, PSM 4097. `AirPodsService.connectToSocket`
uses that transport for AACP. This is not an ordinary public Android RFCOMM/GATT channel.
Upstream `android/README.md` says the L2CAP interoperability fix is OS-dependent and
names some supported devices/builds; it does not certify this Nothing OS beta.

| State | Vendor hook in parser? | What is actually established |
|---|---|---|
| Paired / profile connection | No | Public Bluetooth permissions and profile broadcasts can observe connection; not ownership |
| BLE presence / coarse wear / battery | No | `BLEManager` scan parser; permission, rotating-address association, model layout and stale observations matter |
| AACP battery / ear / listening mode | No | Callbacks exist without vendor flag; requires working AACP on this OS |
| Source MAC / NONE, MEDIA, CALL | No | `AACPManager.receivePacket` invokes parser and callback unconditionally; does not prove buds emit it for ordinary DID |
| Connected host list | No | `0x2E` parsing ungated; actual two-host visibility requires physical test |
| Ownership response / reverse request | No | Callback exists, but safe route release is not available as a general public app operation |
| Local phone Classic MAC | Restricted | Service uses saved manual config, `LOCAL_MAC_ADDRESS` permission, otherwise `su`; unrooted path must use manually recorded address if needed |

**PLAUSIBLE / REQUIRES TESTING:** AACP observation on the specified phone without root.
The project adds no reflection/privileged Android workaround.

## Explicit takeover gate and indirect effects

**VERIFIED IN SOURCE:** `AirPodsService.takeOver`, lines 2450 onward:

1. The special `reverse` branch sends owns=1, media information, reverse relay and
   calls `connectAudio` before reaching the later check. Do not describe all writes as gated.
2. If AACP is connected, the ordinary path returns when `vendor_id_hook` is false **or**
   the observed/cached ownership value equals zero. This is stricter than checking only root.
3. Otherwise it sends owns=1, media information, nearby UI relay, then Hijackv2 to
   other hosts; it attempts A2DP connection and media resume. These are separate actions.
4. Without AACP it follows a BLE/config-dependent reconnection path. That is not proof
   of simultaneous-host routing.

Smart relay builders and inbound source/list callbacks themselves do not test the flag.
Connected-list callbacks can send "new device" media/TiPi messages without it. Firmware
may still restrict the information or routing behavior based on DID and capabilities.
The source alone does not answer whether one Apple-identified host can take over from
an ordinary Android host. This is the central physical experiment.

`onAudioSourceReceived` sends owns=0 for a nonlocal active source when the local MAC is
known. `onOwnershipChangeReceived(false)` pauses and attempts audio disconnect.
`onOwnershipToFalseRequest` handles Hijackv2/ReverseBannerTapped, sends owns=0, pauses,
and attempts route release. These paths do not establish privilege to disconnect A2DP.
Also, upstream updates its ownership cache when sending controls: cached `owns=true`
alone must not become this application's takeover confirmation.

## Public Android route control

**VERIFIED IN SOURCE:** `disconnectAudio`, lines 2956 onward, gates A2DP policy change
on `BLUETOOTH_PRIVILEGED` and headset policy on `MODIFY_PHONE_STATE`, then calls reflected
`setConnectionPolicy`. `connectAudio` also reflects `connect`, including an unprivileged
branch. These are unsuitable production dependencies for this project.

**VERIFIED BY OFFICIAL DOCUMENTATION:** public
[BluetoothA2dp](https://developer.android.com/reference/android/bluetooth/BluetoothA2dp)
exposes profile state, not general connect/disconnect/ownership control for third-party
apps. Its public state does not establish AACP ownership.
[Non-SDK interface restrictions](https://developer.android.com/guide/app-compatibility/restrictions-non-sdk-interfaces)
make reflection an unsupported compatibility strategy.

**NOT SUPPORTED:** promising ordinary-app global A2DP release/reconnect or global media
routing through these public APIs. App-specific audio routing and communication-device
APIs are not arbitrary control of another app's music route.

Any bridge is deferred until a failed genuine multipoint experiment is recorded. A bridge
could report state with explicit pairing and authenticated LAN control, but cannot grant
Bluetooth privileges that Android withholds. It must not collect titles/history or stream audio.
