# Source and host-list refresh research

2026-10-15, source audit only. No hardware experiment or new outbound query is claimed.

| Source | Evidence | Decision |
|---|---|---|
| LibrePods `4589176f7b7943a6091d5c6907c74f441bcf7780`, Android `AACPManager.kt` | Parsers for AUDIO_SOURCE `0x0E` and CONNECTED_DEVICES `0x2E`; SEND_CONNECTED_MAC `0x14` enum; no demonstrated sender for the proposed refresh requests | Keep notifications as the only source of these observations |
| Same revision, Linux `airpods_packets.h` | `0x0D` occurs as a listening-mode **control-command identifier**, nested under opcode `0x09` | Never mistake an ANC-setting packet for a source query |
| `librepods-windows` `a439cadb517f6973b5d57dd2e0d3fbfdde16fe97` | Older port has no implemented current source/list refresh path established in the architecture audit | No format to port |
| `will-ch-h/librepods-windowsbridge` `fbabc879aac40267d6e806470933f6be3695fdd6` | Transport/SDU contract is useful; no refresh sender established by supplemental source search | Transport evidence is not packet-layout evidence |

The existing protocol references identify candidate request opcodes `0x0D` and `0x2D`.
Names alone do not establish whether a request has an empty, indexed, versioned or
otherwise structured body. `0x14` is described as sending connected MAC information,
which is not necessarily a read-only query. No guessed packet builder or polling
loop has been enabled. Existing conservative freshness limits remain unchanged.

Implementation gate for a future refresh feature:

1. Identify a pinned executable sender or authorized protocol trace establishing the
   entire request SDU and its response; retain model/firmware and attribution.
2. Add a sanitized fixture with direction and SDU boundaries. Do not commit captures,
   keys, real host identities, or unrelated payloads.
3. Establish which request refreshes source, host list, wear and ownership separately;
   never update observation time because a request was merely sent.
4. Add bounded, serialized scheduling, cancellation, rate limits, and response expiry.
   Unknown/unsupported firmware must remain disabled; a timeout must not create a
   fresh observation or infer ownership.

This research task is complete to the limit of the available source evidence. Actual
refresh support remains blocked on a packet format, rather than hidden behind a
nonfunctional UI option. The alpha reports that it is waiting for fresh observations.
