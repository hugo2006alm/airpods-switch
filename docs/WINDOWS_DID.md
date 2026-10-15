# Windows DID experiment

## Documented configuration

**VERIFIED BY OFFICIAL DOCUMENTATION:** Microsoft documents four DWORDs under
`HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters` controlling the host's
published Device ID SDP record. Defaults: source=1 (Bluetooth SIG), vendor=6 (Microsoft),
product=1, version=0x0800. Source=2 denotes USB-IF vendor namespace.
[Bluetooth Host Radio Support](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-host-radio-support#how-to-change-the-device-id-profile-record-published-by-windows)

This is a global BTHPORT configuration path, not a per-radio key. Actual Windows 11
driver behavior on USB and integrated radios must be measured separately. The tool
records current OS, selected radios and original values; it never restarts services/radios.

**PLAUSIBLE / REQUIRES TESTING:** source=1, vendor=0x004C as a useful AirPods identity.
The choice matches LibrePods' native `fake_BTA_DmSetLocalDiRecord` field writes
(**VERIFIED IN SOURCE**); it is not a guess based only on the company identifier.
This does not change USB VID/PID, controller manufacturer, BLE manufacturer payload,
hardware firmware, or add Apple host capabilities. It does not prove multipoint.

## Reversible diagnostic workflow

`did status` reads only the four DID values and an allowlist of SupportedServices /
UnsupportedServices. It never enumerates BTHPORT Keys or reads link keys.
`did backup --file <path>` captures machine identity, registry view, key presence and
each value's presence, kind and unexpanded raw data, using exclusive file creation.
Keep backups outside source control; backup integrity and original absence matter.

`did apply-apple --file <path> --confirm APPLY-APPLE` displays the planned writes,
checks administrator status, backup schema/machine/allowlist/integrity and requires
the current state to match the backup. It writes only source=1 and vendor=76 DWORDs;
product/version remain untouched. A partial apply attempts rollback. It never runs at
app startup or during switching.

`did restore --file <path> --confirm RESTORE-DID` restores each original type/value and
deletes values originally absent. Unexpected intervening edits cause refusal, not
silent overwriting. Backups are reusable only for restoration of the same machine.
Local registry tests run against an in-memory store, never the system key.

## What verification means

`did verify` reports **configuration only**. Matching DWORDs are not an SDP observation,
and a remote SDP observation is not confirmation of the identity AirPods actually used.
There is no supported practical local SDP self-query established in this audit.

Use a separate Linux Bluetooth host for external evidence (a normal phone app cannot
query arbitrary Classic SDP through Android's public API):

```sh
sdptool browse --xml WINDOWS_CLASSIC_RADIO_MAC > windows-did.xml
```

Select the PnPInformation / Device ID service (UUID 0x1200), inspect attributes 0x0201
(VendorID), 0x0202 (ProductID), 0x0203 (Version), 0x0205 (VendorIDSource), and retain raw
XML. Verify the SDP tool/version and address correspond to the selected Windows radio.
If absent, retry after an intentional Windows reboot; record before/after. A service
restart alone is not established as equivalent to driver reload. Do not disconnect a
Bluetooth-only keyboard/mouse without another input method.

External SDP verification remains **PLAUSIBLE / REQUIRES TESTING** here. Use identical
procedure before/after applying and after restore. Record PC/radio/driver/OS; repeat on
laptop. Check after reboot and any driver update; persistence and adapter equivalence
are not assumed. Remote AirPods acceptance requires the separate two-host experiment.
