# AirPods Switch

Experimental AirPods-aware switching between Windows 11 and an unrooted Android phone.
Independent project; not affiliated with Apple or the LibrePods authors.

The target is a user-approved AACP ownership transition, confirmed by AirPods source
state and audio-route evidence. A Bluetooth reconnect alone does not count as success.

## Current status

Repository initialized. Research and architecture audit come before implementation.
No switching behavior has been validated on physical hardware.

## Repository strategy

This is a new standalone repository, not a fork. LibrePods and librepods-windows
will be cloned separately under ignored `.research/` directories, with pushes disabled.
Only justified components will be adapted, with license attribution and exact upstream
commit/file tracking. A change to this strategy requires a written architecture decision
demonstrating overwhelming technical justification first.

## Plan

See [implementation plan](docs/PLAN.md). Source findings, official documentation,
local tests, and physical hardware results will be identified separately.

## Visibility

Private initially. The owner can later make it public with GitHub repository settings,
or `gh repo edit hugo2006alm/airpods-switch --visibility public --accept-visibility-change-consequences`.
Review logs, licensing, and secrets before publishing.
