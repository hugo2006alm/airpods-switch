# AirPods 5 source assessment

Evidence: **VERIFIED IN SOURCE**, LibrePods `4589176f7b7943a6091d5c6907c74f441bcf7780`.
References use the [pinned source map](UPSTREAM_MAP.md), not moving branches.
No entry below is a physical hardware claim.

`android/.../data/AirPods.kt`, lines 114–153, explicitly defines two fifth-generation
families. Identification uses the model string received in AACP device information,
not a friendly name such as "AirPods 5" and not an assumption about Pro 2.

| Family | Model numbers | Declared capability difference |
|---|---|---|
| AirPods 5 | A3531, A3532, A3533 | Listening modes, conversation awareness, head gestures, adaptive audio, sleep detection, adaptive volume, stem config |
| AirPods 5 with Wireless Charging Case | A3439, A3440, A3441 | Same plus swipe volume |

Pro 2 separately declares hearing aid and loud sound reduction. Pro 3 adds PPE and HRM.
Those capabilities must not be transferred to AirPods 5 by analogy.

| Switching capability | What source contains | Product evidence / limit |
|---|---|---|
| AACP initialization | Shared 16-byte handshake, host feature flags (`0x4D`, D7 mask), notification registration (`0x0F`) | No AirPods 5-specific branch found; **PLAUSIBLE / REQUIRES TESTING** for this firmware |
| Model / firmware | `INFORMATION` (`0x1D`) parses null-terminated strings | **VERIFIED IN SOURCE**; read actual units before selecting capabilities |
| Battery | `0x04`, left/right/case component IDs | **VERIFIED IN SOURCE** generic layout; disconnected values stay unknown |
| Ear detection | `0x06`, two state bytes | **VERIFIED IN SOURCE** shared parser; confirm left/right/primary mapping on actual model |
| Listening mode | control `0x09`, identifier `0x0D` | **VERIFIED IN SOURCE** and model capability declared; outside switching critical path |
| BLE presence / coarse battery / wear | Manufacturer-data parser, rotating BLE identity and optional encrypted detail | **VERIFIED IN SOURCE**, but BLE model-name table lacks AirPods 5 IDs; **PLAUSIBLE / REQUIRES TESTING** |
| Active source / type | `0x0E`, reversed six-byte MAC, NONE=0/CALL=1/MEDIA=2 | **VERIFIED IN SOURCE** generic parser; receipt under ordinary Android DID unknown |
| Connected hosts | `0x2E`, eight-byte entries, MAC not reversed | **VERIFIED IN SOURCE**; no model-specific multipoint proof |
| Ownership | `OWNS_CONNECTION` control identifier `0x06` | **VERIFIED IN SOURCE**; response distinct from sending a write |
| Smart routing / takeover | `0x10` relay, `0x11` response, Hijackv2 and ownership-false strings | **VERIFIED IN SOURCE**; mixed ordinary Android / Apple-DID Windows topology untested |
| Reverse | ReverseBannerTapped relay, source and ownership callbacks, reconnect action | **VERIFIED IN SOURCE**, but Android profile operations depend on privileges/hidden methods |

BLE model IDs are a different namespace from model numbers and Bluetooth Device ID.
Do not invent fifth-generation BLE IDs or label every Apple manufacturer advertisement
as this user's paired buds. AACP reports, when available, are stronger identity evidence.
The old prose protocol document explicitly describes Pro 2 USB-C firmware 7A305;
that is a format reference, not fifth-generation validation.
