# Controller image candidates added 2026-09-03

The product owner supplied the following local reference images under `docs/images`
for future Tappy controller visuals. They are intentionally ignored by Git and are
not part of the public source tree or a package. Their local presence records design
intent, not protocol support or public-distribution rights. Originals remain
untouched. Each model must receive exact identity matching, protocol evidence, a
coordinate review, hardware testing, and a provenance/rights decision before release
packaging.

## Visual-locator acceptance rules

These rules apply to every controller, keypad, keyboard, button box, MIDI
surface, and other supported input device before its photograph is enabled:

1. Crop the complete device tightly so it fills the locator pane without
   clipping any physical control.
2. Preserve the actual unlit material. Black rubber pads stay black; opaque or
   non-illuminated buttons are never painted white merely for consistency.
3. Neutralize only a region proven to emit user-selectable color. Its neutral
   state must match the real hardware and may be white, gray, black, or another
   material color.
4. Record lighting topology as per-control, zoned, global, fixed-color, or
   unlit. Per-square colors are offered only for verified per-control IDs.
5. Match the overlay to the real illuminated region: full face, center, edge,
   ring, symbol, or another reviewed mask. A button boundary is not evidence
   that its entire face lights.
6. Saved colors remain visible on the photo at rest and brighten on the matching
   physical press. Unsupported controls retain the photograph and receive only
   Tappy's temporary locator highlight.
7. Do not infer behavior from a similar name or newer model. Exact model and
   protocol evidence are required.
8. Continuous controls are defined by type. Faders render a moving cap over a
   neutral channel; bounded potentiometers render a line across their real
   start/end sweep; endless encoders render a line that wraps continuously.
   A whole fader track or knob face must never be used as the moving highlight.
9. Every analog definition records orientation, cap/line geometry, travel or
   sweep limits, input interpretation, and any shared directional control IDs.
   Calibration belongs to the exact physical device profile and travels with an
   exported profile.
10. Bounded controls support captured beginning, optional center, and end raw
    values, including reversed raw ranges. Endless encoders support saved
    direction and degrees-per-input-event tuning because they have no physical
    absolute beginning or end.

| Family | Supplied model images | Current state |
|---|---|---|
| Akai APC MINI | `Akai professional APC mini ver1.jpg`; `Akai professional APC mini MK2 MKII.jpg` | v1: tight neutral-light runtime derivative and fixed candidate layout implemented; mk2: visual candidate only |
| Akai APC Key | `Akai professional APC Key 25 MKI MK1.jpg`; `Akai professional APC Key 25 MKII MK2.jpg` | Visual candidates only |
| Akai APC | `Akai professional APC20.jpg`; `Akai Professional APC40 MK1 MKI.webp`; `Akai professional APC40 MK2 MKII.jpg`; `Akai professional APC64.jpg` | Visual candidates only |
| Akai MPC/MPD | `Akai professional MPC Element.jpg`; `Akai professional MPD16.jpg`; `Akai Professional MPD218.webp`; `Akai Professional MPD24.jpe` | Visual candidates only |
| Native Instruments Maschine | `Native Instruments Maschine Jam.webp`; `Native Instruments Maschine MK1.jpe`; `Native Instruments Maschine MK2 MKII.jpg`; `Native Instruments Maschine MKIII MK3.webp`; `Native Instruments Maschine Plus.webp` | Visual candidates only |
| Native Instruments Maschine Mikro | `Native Instruments Maschine Mikro MK1 MKI.jpg`; `Native Instruments Maschine Mikro MK2 MKII.webp`; `Native Instruments Maschine Mikro MK3 MKIII.jpg` | Visual candidates only |
| Novation Launchpad Mini | `Novation Launchpad Mini MK1 MKI.jpg`; `Novation Launchpad Mini MKII MK2.webp`; `Novation Launchpad Mini MK3.webp` | Visual candidates only |
| Novation Launchpad | `Novation Launchpad MKI MK1.jpe`; `Novation_Launchpad_S.png`; `Novation Launchpad MKII.jpg`; `novation Launchpad X.webp` | Visual candidates only |
| Novation Launchpad Pro | `Novation Launchpad Pro MK1 MKI.jpg`; `Novation Launchpad Pro MK3.webp` | Visual candidates only |

## Active APC MINI v1 derivative

- Local untracked source: `docs/images/Akai professional APC mini ver1.jpg`
- Runtime asset: `src/Tappy.App/Assets/Controllers/akai-apc-mini-v1-neutral.png`
- Built-in image-editing mode: precise object edit
- Prompt intent: tightly frame the full device and neutralize only its verified
  lightable button faces while preserving labels, geometry, and perspective
- Output canvas: `1254 × 1254` PNG
- SHA-256: `B82A52B61B81EEA072A8C1EE2C68835EE7FD1ADB36B27738A17BB462A9171575`
- Runtime role: non-interactive visual locator beside the assignment-square grid
- Continuous-control behavior: all nine APC MINI v1 fader direction pairs share
  one photo visual each. The photo channel is neutralized at runtime, only the
  cap illuminates, and incoming CC 48–56 values move the cap through its travel.

Two attempted true-alpha passes produced opaque checkerboard pixels and were
rejected during QA. They were not copied into the project. The accepted derivative
uses an opaque dark background and its own measured hotspot coordinate system.
