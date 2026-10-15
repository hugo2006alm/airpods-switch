# License and attribution

Combined application license: **GPL-3.0-only**. See [LICENSE](../LICENSE) and
[ADR-002](ADR-002-license-scope.md). LibrePods-derived protocol/model files retain their
upstream GPL-3.0-or-later grant; that permits combining under GPL version 3.

LibrePods source files carry `Copyright (C) 2025 LibrePods contributors` and explicitly
permit GPL version 3 or any later version. Both audited primary repositories contain
the GPLv3 text. Required protocol layouts and model data are selectively adapted;
copyright and SPDX notices appear in affected files. Exact source mapping is maintained
in [UPSTREAM_MAP.md](UPSTREAM_MAP.md).

Windows bridge transport adaptation credits its contributors and repository. Its audited
root license is GPLv3; no later-version grant was established for the new transport file,
so this component and the combined application use GPL-3.0-only conservatively.
Before distribution, preserve notices, include this license and the corresponding
source for the distributed GPL program, including local changes. No upstream binary,
Apple artwork, font, firmware, key material, or entire application is bundled.

The GPL text in this repository is a verbatim copy of the audited LibrePods `LICENSE`.
This license document's FSF copyright is separate from the software contributors' notices.

NuGet dependency licenses and pinned versions are recorded in committed `packages.lock.json`
files. NAudio.Wasapi/NAudio.Core 2.2.1 (Mark Heath and contributors, MIT) provides Core
Audio observation; [upstream license](https://github.com/naudio/NAudio/blob/v2.2.1/license.txt).
Microsoft.WindowsAppSDK 2.5.1 provides notifications, isolated in the tray project.
Its open-source repository has MIT-licensed source, but **the restored NuGet binary
package carries Microsoft Windows App SDK software license terms**, not an MIT-only
package license. The exact package license and NOTICE are preserved in `third_party/`.
The application's GPL does not relicense those unmodified Microsoft runtime binaries;
retain their terms/notices and verify the chosen redistribution arrangement before a
public binary release. No Microsoft source is copied or modified by this project.
.NET/Windows SDK/runtime packages retain their distributed license/notice files.
`scripts/publish.ps1` includes GPL, this attribution file and `third_party/`; keep runtime/package notices
and supply corresponding application source at the same revision if distributing binaries.
Installed third-party profile drivers remain separate system prerequisites: availability,
publisher, signature policy, and device binding must be checked on each test PC.

No code was copied from the unlicensed WinPods research tree.
