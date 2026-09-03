# Compatibility and support tiers

## Initial platform

Tappy targets Windows 11 x64 and .NET 8. The implemented inputs are Windows Raw Input
keyboard-class top-level collections and a dedicated provider for the physical
Logitech G13 `046D:C21C`, `FF00:0000` vendor-HID collection. Generic learned raw HID,
MIDI, encoder, and joystick providers are not implemented. The G13 stick exposes four
fixed-threshold directional controls; arbitrary analog profile values, deadzones,
and user-configurable thresholds remain future work.

## Source behavior

All initial controllers operate in **Device-aware pass-through** mode. Tappy routes
the selected physical source independently but does not suppress its ordinary
Windows or vendor-software behavior. A keyboard-class original key may still reach
the foreground application; parallel G HUB behavior is likewise outside Tappy's
control. This is best suited to a spare controller and harmless outputs such as
F13–F24. A second full keyboard is not completely or exclusively remapped.

## Evidence-based tiers

| Tier | Meaning | Current devices |
|---|---|---|
| Architecture-ready | Provider/layout boundary exists, without device proof | Raw keyboard-class controls and generic future provider seams |
| Enumerated | Windows listed an exact sanitized logical device; no controls were captured | Freewolf K15 candidate: one authoritative ContainerId group, `1A2C:2D43`, four Raw Input keyboard interfaces, reported totals 56/264. User-identified Targus numberpad candidate: `05A4:9862`, one keyboard interface, reported total 264. Windows-identified Razer Tartarus: `1532:0201`, two grouped keyboard interfaces, reported total 264. Logitech G13: one ContainerId group, exact `046D:C21C`, `FF00:0000`, one vendor-HID interface. |
| Code-supported | Exact provider/layout behavior passes deterministic tests, without physical control proof | Logitech G13: dedicated eight-byte report decoder/provider and 39-control code-defined tile grid. K15, Targus numberpad, and Tartarus keyboard collections: generic grouped-keyboard path only. |
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

The images under `PAD IMAGES` are visual references and do not move any product into
a support tier. Shared VID/PID, shells, labels, or marketing names are not protocol
evidence. Processed derivatives remain excluded from source and packages until
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
  lifecycle, fault, and unplug cleanup.

These tests validate software behavior only. 6KRO/NKRO capability, ghosting,
consumer-control collections, identical serial-less devices, reconnect stability,
sleeping wireless receivers, Windows lock/suspend, and latency targets require real
hardware evidence before support claims.

The current automated suites pass with a zero-warning Release build: Core 46,
Windows 103, App 62, G13 HIL tool 23, and Output Witness 53 (287 total).
Packaged-artifact checkpoint status is in [`TESTING.md`](TESTING.md). Every physical
or manual check remains a separate gate.
The current twelve-project NuGet advisory query reports no known vulnerable packages;
that point-in-time result is not a complete security audit.

## Known Windows limits

- Raw Input is observational and cannot selectively suppress a chosen physical
  keyboard system-wide.
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

See [`LOGITECH_G13.md`](LOGITECH_G13.md) for the exact G13 code/evidence boundary and
primary sources.
