# Implementation plan

## Constraints

- Keep Android unrooted; no Xposed, hidden-API production dependency, system app, or firmware changes.
- Keep this repository independent. Do not modify either research upstream.
- Distinguish pairing, profile/audio/control links, ownership, source, and routing.
- Protect calls; ask before taking over media; suppress repeated prompts and retries.
- Never change DID automatically. Require exact backup, explicit confirmation, and reversible restoration.
- Claim hardware success only after source identity and actual playback evidence.

## Stages

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
