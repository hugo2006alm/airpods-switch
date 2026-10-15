# ADR-002: GPLv3 scope for the combined application

Status: **Accepted**. Date: 2026-10-15.
Refines the license selection in [ADR-001](ADR-001-application-architecture.md);
repository and application architecture remain independent and unchanged.

## Context

LibrePods protocol/model files explicitly permit GPL version 3 or later. The supplemental
Windows bridge transport carries a root GPLv3 license, but the audited new file has no
explicit later-version grant. The Microsoft Windows App SDK source repository and its
binary NuGet package do not carry identical license texts.

## Decision

License this combined application's original code and adapted driver transport under
**GPL-3.0-only**. Preserve original LibrePods contributors' copyright and GPL-3.0-or-later
grant in the protocol/model adaptations. All are compatible with using GPL version 3
for this application. Do not infer permission to redistribute the driver adaptation under
future GPL versions. Include corresponding application source and all notices when
distributing the application, and track exact upstream files/commits.

Keep Microsoft runtime binaries unmodified and outside the application's source license.
Preserve exact NuGet license/NOTICE texts; do not label the binary package MIT-only based
on its source repository. Local self-contained artifacts are test outputs. Public binary
packaging requires checking its runtime redistribution terms; do not automatically publish
a release as part of this task. NAudio retains its MIT license.

## Consequences

This narrows the provisional project-level license selection without deleting any
upstream notice or importing an entire repository. No forks or upstream edits are needed.
Future adapted components require the same license/source-map inspection before reuse.
