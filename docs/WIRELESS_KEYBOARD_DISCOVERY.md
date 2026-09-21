# Wireless keyboard names

Tappy now discovers names in the background, without registering a second input
listener or changing device pairing, mappings, lighting, or Windows security.
The existing Raw Input keyboard path accepts Windows-paired Bluetooth keyboards
and number pads as well as USB-connected keyboard sources.

## Logitech Unifying

For the verified USB receiver **046D:C52B**, Tappy locates only the HID++ short and
long vendor collections belonging to the exact same Windows ContainerId as the
keyboard source. It sends receiver-addressed **GET LONG REGISTER (83 B5)** requests
for pairing information (20–25) and device names (40–45). No SET REGISTER, pairing,
unpairing, notification, DJ mode, lighting, or remapping commands are sent.

Keyboard and number-pad pairings are named; mouse/touchpad pairings are excluded
from the keyboard label. Multiple paired keyboards are listed explicitly. Paired
does **not** mean switched on or currently connected. Unifying keyboards still
share the existing receiver-level Tappy input/protection boundary. Discovery does
not create pretend independently routable keyboard entries.

Reads use shared overlapped handles, validated report sizes, matching reply headers
and slot numbers, per-request timeouts, cancellation, and a five-second overall
budget including the process-wide query gate. An incomplete/failed scan falls back
to the receiver name. Discovery does not block the UI or input message thread.

## Bluetooth

Pair the device using Windows Bluetooth settings first. Tappy resolves the exact
Raw Input interface to its Windows PnP node, follows a bounded ancestor chain, and
reads the paired Bluetooth device's friendly name from an exact shared container.
This matters for the Microsoft pad: its HID product string says **Microsoft
Keyboard for Bluetooth**, whereas the paired-device node correctly says
**Microsoft Bluetooth Number Pad**. The HID service and paired device can be
siblings, not parent/child. Empty/system containers are never used to borrow a name.
Bluetooth Classic and BLE naming paths are covered; host-radio names and other
paired devices' names are never substituted.

Reported strings are sanitized, bounded, and display-only. Existing persistent
IDs, fingerprints, mapping ownership, confirmation gates, and typing protection
remain unchanged—including existing identities whose Bluetooth VID/PID parsing
does not recognize the bus-specific identifier format. Names are not a model
compatibility certificate and do not select a photo/layout by themselves.

### Microsoft X821504-001 visual layout

The observed Bluetooth HID service signature (Microsoft 045E / product 0764)
now supplies a separate, display-only model hint. It selects the owner's existing
`PAD IMAGES/Microsoft X821504_001 product id 02010_486_0126212_00934.png` photo
and all 17 keys immediately after confirmation. Friendly names alone cannot
select this layout; grouped interfaces must agree on the model signature.

Keys follow the photograph's four-column, five-row arrangement: tall **+** and
**Enter**, wide **0**, and a readable **Num Lock** label. The saved TESTBED reports
confirm base Num Lock, E0 slash/Enter, and base numeric scan codes. The old generic
press-order geometry is repaired once without changing control IDs or assignments;
later custom positions and sizes are retained. Additional reports stay separate,
to the right, and can share the corresponding physical photo highlight.

The source photograph is copied unchanged into the app. A render-only viewport
removes empty side margins; black keys retain their appearance. There is no claim
of RGB lighting support, Num Lock LED readback, or per-device suppression.

## Refresh and diagnostics

Results appear on the normal workspace poll after background discovery finishes.
Refresh can retry a cached query after 30 seconds; removed interface entries are
discarded, including late results. No private device paths or Bluetooth addresses
are added to the public probe output. Discovery failure never prevents normal
keyboard enumeration.

`Tappy.DeviceProbe --json` remains descriptor-only. The explicit
`--wireless-names` option additionally performs the metadata/receiver information
queries; its privacy notice reflects that. Run inventory in the interactive user
session: Windows Raw Input inventory from an SSH service session can be empty.

## TESTBED evidence, 2026-09-18

The explicit wireless-name probe, run in the logged-in TESTBED user session,
completed with exit code 0 and identified:

- **Microsoft Bluetooth Number Pad — Bluetooth**, paired by the user during this
  test. Windows reports Microsoft vendor 045E/product 0764 in its Bluetooth service
  instance metadata. The user's physical part number is X821504-001; no printed
  part-number-to-device-ID heuristic was added.
- Unifying receiver paired-keyboard records for **Logitech K400 Plus** and **K350**.
  No pairing was removed and neither device was claimed to be awake from this read.
- **Razer Tartarus keypad**, unchanged.

All three public identity fingerprints remained unchanged across naming updates.
These are live naming/inventory results, not a physical key-by-key, sleep/reconnect,
suppression, or driver-certification result. The Bluetooth pad still needs its
normal Identify/press/release confirmation and physical acceptance tests in Tappy.

The subsequent descriptor-only Microsoft layout probe exited 0 and returned
`presentationModelId: microsoft-bluetooth-number-pad-0764` for that same Microsoft
fingerprint, with no model hint for the Tartarus or Unifying receiver. It made no
HID++ query and captured no input. Local evidence is under
`artifacts/microsoft-keypad-20260918/testbed-model-inventory.json`.
All 557 solution tests and both portable readiness audits passed. New tests cover
17-key prepopulation, saved-assignment migration, custom geometry persistence,
separate alternate reports, photo selection/press highlighting, and disjoint
tile/photo rectangles. Dark, light, and high-contrast renders were generated;
the dark full-window and all-face alignment renders were visually inspected.
The staged build is `C:\TappyLab\20260918-microsoft-keypad\app\Tappy.exe`, with
desktop shortcut **Tappy - Microsoft Keypad**. This is not a physical key-by-key
acceptance or certification result.

Protocol references (implementation here is independent C#, not imported source):

- [HID++ receiver register definitions](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10_constants.py)
- [Receiver pairing/name register use](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/receiver.py)
- [Windows device friendly names](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-friendlyname)
- [Windows HID transports](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-transports)
