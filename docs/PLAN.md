# Implementation plan

## Constraints

- Keep Android unrooted; no Xposed, hidden-API production dependency, system app, or firmware changes.
- Keep this repository independent. Do not modify either research upstream.
- Distinguish pairing, profile/audio/control links, ownership, source, and routing.
- Protect calls; ask before taking over media; suppress repeated prompts and retries.
- Never change DID automatically. Require exact backup, explicit confirmation, and reversible restoration.
- Claim hardware success only after source identity and actual playback evidence.

## Stages

Current implementation status (2026-10-15):

| Stage | Status |
|---|---|
| Repository / research / license / ADR | Complete; independent public repository, pinned read-only references |
| Bluetooth diagnostics | Implemented and locally exercised; one radio, no paired AirPods on development host |
| AACP initialization, parser, model, battery, ear | Implemented; synthetic tests pass; live transport/firmware gate pending |
| Source / host list / ownership | Notification parsing/store implemented; exact query bodies still unresolved |
| DID diagnostics | Status/backup locally exercised; apply/restore tested on fake stores only; physical SDP gate pending |
| Manual takeover | User-confirmed minimal experiment implemented; source+render confirmation required; no hardware result |
| Windows media / Core Audio | Implemented and locally observed; unknown AUMID correlation conservatively fails confirmation |
| Coordinator simulations | Implemented and tested |
| Tray / native actionable notifications | Built; headless native startup and visual/hardware activation checks tracked separately |
| Android bridge | Deliberately deferred; no failed genuine two-host experiment yet |

Required user test: [HARDWARE_EXPERIMENT_01.md](HARDWARE_EXPERIMENT_01.md).
Windows alpha engineering now includes a versioned binary/source package and manual
draft-release workflow, readiness checks, explicit recovery after sleep or adapter/output
loss, redacted support ZIP export (tray and CLI), and process AUMID correlation. PR CI
builds packages and exercises published native startup. See [alpha guide](ALPHA_GUIDE.md).
Source refresh research remains [blocked on exact packet evidence](REFRESH_QUERY_RESEARCH.md).
Remaining engineering after evidence: safe refresh queries, full routing metadata if
needed, verified driver/direct transport packaging, hardware-proven endpoint correlation,
and long-running reliability. The technical MVP is **not yet hardware-complete**.

1. Repository initialization, research checkout pinning, license inspection.
2. Audit current AirPods models, Android gates, AACP routing, and Windows experiment.
3. Research official Windows APIs and DID; write feasibility and architecture ADR.
4. Implement Bluetooth diagnostics and honest AACP capability reporting.
5. Implement protocol parsing, state store, simulation and switching policy tests.
6. Implement reversible DID diagnostics and external verification procedure.
7. Prepare genuine takeover experiment; implement viable transport only after API audit.
8. Implement independent Windows media monitoring and audio-route observation.
9. Wire coordinator and tray/notifications when transport feasibility permits.
10. Consider Android bridge only if physical experiment disproves useful two-host routing.

## Required reports

`FEASIBILITY.md`, `AIRPODS5.md`, `ANDROID_LIMITATIONS.md`, `AACP_SWITCHING.md`,
`WINDOWS_DID.md`, `ADR-001-application-architecture.md`, `HARDWARE_EXPERIMENT_01.md`,
`DEBUGGING.md`, `THIRD_PARTY.md`, `UPSTREAM_MAP.md`.

## Evidence labels

- VERIFIED IN SOURCE
- VERIFIED BY OFFICIAL DOCUMENTATION
- VERIFIED LOCALLY
- VERIFIED ON PHYSICAL HARDWARE
- PLAUSIBLE / REQUIRES TESTING
- NOT SUPPORTED

Record exact references and scope for every important conclusion.
