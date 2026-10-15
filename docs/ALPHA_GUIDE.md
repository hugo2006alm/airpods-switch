# Windows alpha setup

This alpha contains an experimental Windows tray app and diagnostic CLI. AirPods
switching has not been verified on physical hardware. Windows 11 x64 is required;
the .NET runtime and Windows App SDK runtime are included. No root, administrator
startup, driver installer, automatic pairing, automatic DID edit, or Android app is
included. Binaries are currently unsigned.

Download the Windows ZIP and matching source ZIP from a reviewed prerelease, or the
`windows-alpha` artifact from a successful PR/main CI run. Extract the whole Windows
ZIP to a writable folder. Keep every DLL, runtime file, license, notice and `docs/`
directory. Launch `tray/AirPodsSwitch.Tray.exe`. The CLI is `diag/airpods-diag.exe`.
`build-info.json` identifies version, exact source commit and hardware-validation status;
the separate SHA256SUMS file checks archive integrity. Checksums are not a signature.

## Guided setup

1. Open Bluetooth settings from the app. Pair and connect the AirPods explicitly.
2. Refresh devices and select the Bluetooth radio and paired AirPods. Renamed audio
   devices can appear too; verify you selected the intended earbuds.
3. Open Sound settings, identify the matching AirPods output, select it, and check
   the confirmation box. Refreshing or changing a selection requires confirmation again.
   The app does not choose or change Windows' default output for you.
4. Choose a control connection. **Installed control driver** requires an already
   installed compatible AAP profile driver with exactly one interface for these AirPods,
   and one radio. An ordinary audio connection does not provide this interface.
   The app explains this prerequisite and never downloads or installs a driver.
   **Direct Bluetooth (experimental)** attempts Windows L2CAP with explicit radio binding;
   it can fail even when the radio is on. Do not change DID just to work around a transport error.
5. When setup reports ready, Connect. The app will wait for model, wear, source,
   connected-host and media evidence. Accept an eligible Switch offer in the window or
   notification. Commands sent and control connectivity are not success evidence.

The source/host-list refresh requests remain undocumented in the audited sender code.
This alpha consumes notifications; when evidence expires it suppresses switching and
shows a waiting message. Repeated connecting is not a substitute for confirmed protocol
support. See [refresh research](REFRESH_QUERY_RESEARCH.md).

## Interrupted connections

Sleep cancels the current session and pending offers. Resume never reconnects or replays
a switch. A radio turning off, device removal/unpairing, missing output, receive failure,
or observation timeout also ends the session. Refresh devices, confirm the output, and
Connect to start fresh. No accepted notification from an older connection can start a
new switch. Calls and microphone capture continue to veto takeover.

Stop closes the connection. Closing the window hides it in the tray; Quit exits.
The application does not install an autostart entry.

## Support bundle

Choose **Export diagnostics…** and a new ZIP filename. Export is local; the app never
uploads it. It includes version/OS, selection flags, capability counts, and reconstructed
recent log event metadata. It excludes device/host names, MACs, app IDs, titles, paths,
request IDs, raw/free-form messages, packet payloads, settings files, registry backups
and keys. Up to six recent segments are sampled (last 1 MiB and 4,000 lines per segment).
Malformed/truncated lines are omitted. It is a support summary, not a Bluetooth capture.
An existing file is never overwritten.

If you cannot use the tray, run `diag/airpods-diag.exe support --file <new-diagnostics.zip>`.
This exports the same redacted event schema with current capability counts and no
tray settings. It does not connect to an AirPods control service or edit the registry.

Keep the source ZIP with distributed binaries. GNU GPL application source and bundled
dependency notices are included; third-party runtime binaries retain their own licenses.
The full physical procedure is [Hardware Experiment 01](HARDWARE_EXPERIMENT_01.md).
