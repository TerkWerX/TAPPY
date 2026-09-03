# Logitech G13 support boundary

> Status: code-supported with an attended all-control visual-response report; formal finite HIL evidence pending
>
> Exact physical identity: USB `046D:C21C`, Raw Input `RIM_TYPEHID`, usage
> page/usage `FF00:0000`

Tappy has a dedicated input provider for the physical Logitech G13 vendor-HID
collection. This is a narrow, model-specific implementation—not generic learned HID
support and not a claim that every Logitech gaming device uses the same protocol.

## Identity and implemented scope

The schema-3 descriptor probe observes exactly one G13 logical controller, grouped
by its authoritative Windows ContainerId, with one Raw Input interface in the
current hardware session. The provider requires all of `046D:C21C`, `RIM_TYPEHID`,
and `FF00:0000`. Logitech G HUB's separate `046D:C232` virtual keyboard is explicitly
excluded from both physical-keyboard selection and G13 identity.

The accepted input shape is one eight-byte report with report ID `01`, two joystick
bytes, and five button bytes. Tappy exposes 39 code-defined controls in a stable
code-rendered tile grid:

- G1–G22;
- five LCD/menu buttons;
- M1, M2, M3, and MR;
- joystick left-side, bottom-side, and press controls;
- the Lights button; and
- four discrete stick directions derived from X/Y with conservative hysteresis.

The raw X/Y state remains provider-specific; profiles currently map the four
direction controls, not arbitrary analog values. Tappy does not send G13 feature or
output reports and does not control its LCD, backlight, or memory LEDs.

The G13 follows the same explicit-selection safety contract as keyboard controllers:
enumeration never arms it; selection must observe a well-formed neutral frame plus a
complete press/release before confirmation; Rehearsal Mode produces no output; and
unplug, lifecycle, fault, or emergency cleanup synthesizes releases for state Tappy
owns. Profiles preserve the provider-specific control identities and the 39-control
layout.

## Evidence boundary

Deterministic Windows and App tests cover report validation, all defined button
bits, joystick direction thresholds, simultaneous transitions, exact device
identity, `C232` exclusion, deliberate confirmation, mapping/profile round-trip,
and cleanup. Another 23 deterministic tests cover the finite verifier's state
machine, exact-device/explicit-arm refusal, interruption handling, and aggregate
evidence contract. During the final accessibility-build preflight, the operator
selected, identified, and confirmed the attached G13 with Rehearsal Mode checked,
then reported that every G13 control responded visually in Tappy. The ignored local
run record preserves the statement and screenshot. This is useful attended evidence,
but it is not the finite verifier: no aggregate verifier record, mapped-output run,
pass-through witness, unplug-while-held run, or completed HIL record exists, so the
device is not Functional or Verified.

The finite verifier and its safeguards are described in
[`HARDWARE_TEST_STATION.md`](HARDWARE_TEST_STATION.md). It must be explicitly armed;
until that run succeeds, “code-supported” is the strongest accurate label.

## Primary protocol and platform sources

- Microsoft's [`RID_DEVICE_INFO`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rid_device_info)
  distinguishes `RIM_TYPEHID` from keyboard and mouse input, while
  [`RID_DEVICE_INFO_HID`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rid_device_info_hid)
  exposes VID, PID, usage page, and usage for the top-level collection.
- Microsoft's [`RAWHID`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawhid)
  defines the size/count/raw-data envelope delivered through Raw Input.
- Microsoft's [`DEVPKEY_Device_ContainerId`](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid)
  documents the physical-device grouping evidence used by Tappy; matching VID/PID
  alone is never used to merge interfaces.
- The upstream Linux HID identifiers list the G13 as product `C21C` in
  [`hid-ids.h`](https://github.com/torvalds/linux/blob/940de590b839f71d6dc846160534bf202401b8b7/drivers/hid/hid-ids.h#L938),
  and the upstream G13 driver documents the report ID/joystick/button shape and
  event interpretation in
  [`hid-lg-g15.c`](https://github.com/torvalds/linux/blob/940de590b839f71d6dc846160534bf202401b8b7/drivers/hid/hid-lg-g15.c#L65-L69)
  and its
  [G13 event path](https://github.com/torvalds/linux/blob/940de590b839f71d6dc846160534bf202401b8b7/drivers/hid/hid-lg-g15.c#L691-L734).

Tappy's implementation was independently written from these public platform and
protocol facts; Linux implementation code was not copied. An owner-authorized
internal donor audit found no reusable G13 or SpacePilot implementation, and no
donor code, repository history, or logs were copied into Tappy.

The 3Dconnexion SpacePilot Pro is a separate vendor-HID candidate. It can eventually
reuse Raw Input transport, explicit selection, identity, lifecycle, and cleanup
concepts, but it does not share the G13 report protocol or decoder.
