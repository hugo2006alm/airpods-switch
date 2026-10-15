# AirPods Switch

Experimental AirPods-aware switching between Windows 11 and an unrooted Android phone.
Independent project; not affiliated with Apple or the LibrePods authors.

The target is a user-approved AACP ownership transition, confirmed by AirPods source
state and audio-route evidence. A Bluetooth reconnect alone does not count as success.

## Current status

Research audit, architecture decision, diagnostic CLI, protocol parsers, explicit state
store, switching policy, reversible DID tools, Windows media/audio observers, and an
experimental tray/notification host are implemented. This is a diagnostic MVP awaiting
physical validation, not a completed seamless-switching product.

| Category | Status |
|---|---|
| Works | No AirPods switching feature has been verified on physical hardware yet |
| Experimental | `airpods-diag`, profile-driver AACP transport, direct L2CAP experiment, state watch/manual takeover, tray and actionable notifications |
| Verified locally | Windows radio/adapter/audio inventory, DID inspection and exact backup, media playback debounce; deterministic parser/policy/registry tests and clean build |
| Requires test | Actual AirPods 5 firmware, unrooted Nothing OS AACP, two-host persistence, DID publication/acceptance, ownership/source transition, audible Windows output, notifications and reverse behavior |
| Does not work / unsupported | Stock Qt Windows L2CAP path; generic public-API Android A2DP release/reconnect; root-dependent production path; automatic DID edits; source/list query bodies not yet established |

Important limitation: observations expire conservatively (source 5 s, wear/list 15 s).
Safe source/list refresh queries still need protocol evidence. If state expires, the app
suppresses switching. It never reports sending an ownership command or selecting an
audio output as takeover success.

## Start here

1. Read [technical assessment](docs/FEASIBILITY.md), [AirPods 5 matrix](docs/AIRPODS5.md),
   [Android limits](docs/ANDROID_LIMITATIONS.md), and [architecture ADR](docs/ADR-001-application-architecture.md).
2. Follow [build and command guide](docs/BUILD.md).
3. Run [hardware experiment 01](docs/HARDWARE_EXPERIMENT_01.md) on desktop and laptop.
4. Return logs/results listed there. Preserve the distinction between source, official
   documentation, local software tests and physical observations.

With .NET SDK 10.0.402 installed:

```powershell
.\scripts\build.ps1
.\scripts\diag.ps1 inventory
.\scripts\diag.ps1 media --seconds 15
.\scripts\diag.ps1 replay --file tests/fixtures/two-host.hex
```

This checkout also has a local SDK under ignored `.tools/dotnet`. Use
`scripts/build.ps1 -Dotnet "$PWD\.tools\dotnet\dotnet.exe"` here. `diag.ps1` detects it.
See [local validation](docs/LOCAL_VALIDATION.md) for measured scope.

## Repository strategy

This is a new standalone repository, not a fork. LibrePods and librepods-windows
will be cloned separately under ignored `.research/` directories, with pushes disabled.
Only justified components will be adapted, with license attribution and exact upstream
commit/file tracking. A change to this strategy requires a written architecture decision
demonstrating overwhelming technical justification first.

## Plan

See [implementation plan](docs/PLAN.md). Source findings, official documentation,
local tests, and physical hardware results will be identified separately.

## Architecture and license

Independent .NET 10 core, Windows integration, diagnostic CLI and minimal WinForms tray.
Windows App SDK handles notifications. Bluetooth/AACP, media, rendering, DID and policy
remain separate. No Android fork or bridge is included; bridge work requires a failed
genuine-multipoint experiment first.

GPL-3.0-only for the combined application; upstream components retain their stated grants.
[License decision](docs/ADR-002-license-scope.md), [attribution](docs/THIRD_PARTY.md) and [upstream file/SHA mapping](docs/UPSTREAM_MAP.md)
cover selective LibrePods adaptation. Apple assets/fonts and upstream applications are
not vendored. No drivers, private keys or registry backups are committed.

## Visibility

Private initially. The owner can later make it public with GitHub repository settings,
or `gh repo edit hugo2006alm/airpods-switch --visibility public --accept-visibility-change-consequences`.
Review logs, licensing, and secrets before publishing.
