# Compatibility and support tiers

## Initial platform

Tappy targets Windows 11 x64 and .NET 8. The implemented inputs are Windows Raw Input
keyboard-class top-level collections, a dedicated provider for the physical
Logitech G13 `046D:C21C`, `FF00:0000` vendor-HID collection, and selected WinMM MIDI
input ports. Generic learned raw HID, encoder, and joystick providers are not
implemented. The G13 stick exposes four
fixed-threshold directional controls; arbitrary analog profile values, deadzones,
velocity rules, MIDI SysEx/clock, and user-configurable thresholds remain future work.

## Source behavior

All currently built controllers operate in **Device-aware pass-through** mode. Tappy routes
the selected physical source independently but does not suppress its ordinary
Windows or vendor-software behavior. A keyboard-class original key may still reach
the foreground application; parallel G HUB behavior is likewise outside Tappy's
control. This is best suited to a spare controller and harmless outputs such as
F13–F24. A second full keyboard is not yet completely or exclusively remapped.

An optional signed-filter architecture is under development. Its deterministic gate
already requires a distinct stable protected primary keyboard, exact secondary
identity, signed/HVCI-valid backend, authenticated broker, role acknowledgements,
working recovery paths, and a current kernel fail-open heartbeat. No driver or broker
is currently built, installed, or enabled, so the effective behavior remains
pass-through. See [exclusive keyboard input](EXCLUSIVE_KEYBOARD_INPUT.md).

## Evidence-based tiers

| Tier | Meaning | Current devices |
|---|---|---|
| Architecture-ready | Provider/layout boundary exists, without device proof | Raw keyboard-class controls and generic future provider seams |
| Enumerated | Windows listed a sanitized logical device; no controls were captured | Freewolf K15 candidate: one authoritative ContainerId group, `1A2C:2D43`, four Raw Input keyboard interfaces, reported totals 56/264. User-identified Targus numberpad candidate: `05A4:9862`, one keyboard interface, reported total 264. Windows-identified Razer Tartarus: `1532:0201`, two grouped keyboard interfaces, reported total 264. Logitech G13: one ContainerId group, exact `046D:C21C`, `FF00:0000`, one vendor-HID interface. APC MINI: one WinMM MIDI input port that Tappy enumerated and opened; WinMM exposes no serial/port identity, so confidence remains Ambiguous. |
| Code-supported | Provider/layout behavior passes deterministic tests, without complete physical control proof | Logitech G13: dedicated eight-byte report decoder/provider, 39-control tile grid, owner-photo visual locator with one exact hotspot per control, and exact-device global RGB backlight synchronization. Original APC MINI: generic WinMM note, CC-direction, and program-change provider; exact-name fixed 99-direction grid; photo locator; complete Tappy assignment pipeline; attended physical note-input spot check. K15, Targus numberpad, and Tartarus keyboard collections: generic grouped-keyboard path only. |
| Functional | Make/break/repeat/state/mapping checks passed on hardware | None |
| Verified | Controller Passport and HIL evidence passed review | None |

The latest descriptor-only schema-3 probe reports eight logical controllers in the
current session: seven grouped keyboard controllers (including the K15, Targus
candidate, and Tartarus) and one G13. It
excludes the `046D:C232` G HUB virtual keyboard from both keyboard and G13 identity.
No K15, Targus, or Tartarus key events or G13 button/stick events were captured by
that descriptor-only probe. Separately, the operator reported visual response from
every G13 control during an attended Rehearsal preflight; that observation does not
replace the finite verifier or promote the device to Functional. Windows also
exposes non-keyboard Tartarus collections; the current
generic provider does not claim their mouse, consumer-control, system-control, or
vendor-HID behavior.

The APC MINI spot check demonstrated real note events, persistent tiles, and
selection/illumination in Tappy. It did not enumerate every pad/button/fader against
its printed label, so it does not yet satisfy the Functional tier. The exact model
boundary and remaining finite test are documented in
[AKAI_APC_MINI_V1.md](AKAI_APC_MINI_V1.md).

Images do not move any product into a support tier. Shared VID/PID, shells, labels,
or marketing names are not protocol evidence. The owner's approved G13 photo is a
visual locator only and exact identity still comes from the implemented provider.
Other `PAD IMAGES` derivatives remain excluded from source and packages until
provenance, usage rights, exact-model and protocol evidence, processing history, and
explicit approval are recorded.

## Semantics covered by design and deterministic tests

- Scan-code identity with E0/E1 distinctions.
- Separate top-row/numpad/navigation identities when Raw Input reports distinct
  scans/extended flags.
- Left/right modifier and numpad Enter distinctions.
- Make, break, OS autorepeat, simultaneous state, and multiple session isolation.
- Device removal with synthetic owned-output release.
- Self-injected event rejection and bounded recursion/output rate.
- Profile isolation and controller/layout/layer/binding round-trip.
- Authoritative ContainerId grouping, stable reconnect identity, and no VID/PID-only
  heuristic merging.
- Exact G13 identity and `C232` exclusion; strict eight-byte/report-ID validation;
  all defined button bits; joystick hysteresis/directions; provider confirmation;
  39-control model layout/tile-grid/profile mapping; ordered quick-tap visuals; and
  lifecycle, fault, and unplug cleanup. Its one shared RGB backlight uses an exact
  `046D:C21C` feature-report target and never presents per-key color controls.
- MIDI note-on/off and velocity-zero release normalization; device-scoped channel/note
  identity; balanced CC increase/decrease and program-change pulses; duplicate-device
  ambiguity; busy-port failure; explicit confirmation; and routing an incoming MIDI
  pad through keyboard, text, MIDI, and OSC steps in the ordinary action engine.

These tests validate software behavior only. 6KRO/NKRO capability, ghosting,
consumer-control collections, identical serial-less devices, reconnect stability,
sleeping wireless receivers, Windows lock/suspend, and latency targets require real
hardware evidence before support claims.

The current automated suites pass with a zero-warning Release build: Core 58,
Windows 134, App 109, Input Broker 16, G13 HIL tool 23, and Output Witness 53
(393 total).
Packaged-artifact checkpoint status is in [`TESTING.md`](TESTING.md). Every physical
or manual check remains a separate gate.
The current twelve-project NuGet advisory query reports no known vulnerable packages;
that point-in-time result is not a complete security audit.

## Known Windows limits

- Raw Input is observational and cannot selectively suppress a chosen physical
  keyboard system-wide. The optional driver track is the only planned mechanism for
  that behavior; it is not a current capability.
- SendInput is not firmware USB HID and may be rejected across integrity levels, on
  secure desktops, or by exclusive/anti-cheat software.
- Hardware cannot report simultaneous states it does not physically support; Tappy
  never infers missing NKRO/6KRO events.
- Some media or vendor controls arrive through different top-level collections.
  Tappy groups them only with strong container evidence and otherwise shows them
  separately rather than duplicating or guessing.
- Dedicated G13 support does not imply support for another vendor-HID product.
  In particular, the 3Dconnexion SpacePilot Pro may reuse transport and lifecycle
  concepts later but has a different, unimplemented protocol.
- WinMM MIDI device identity is name/manufacturer/product/driver based and cannot
  prove a serial number or USB port. Identical MIDI units are shown separately but
  remain ambiguous across reorder/reconnect. Whether two applications may open the
  same MIDI input simultaneously is driver-dependent.

See [`LOGITECH_G13.md`](LOGITECH_G13.md) for the exact G13 code/evidence boundary and
primary sources.
Driver-specific compatibility policy is in
[`ANTI_CHEAT_COMPATIBILITY.md`](ANTI_CHEAT_COMPATIBILITY.md).
