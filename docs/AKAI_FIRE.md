# Akai Professional Fire support boundary

> Status: buttons, pads and encoders are code-supported; all three now have a
> measured photo locator (the encoders' since 2026-09-20, not yet physically
> confirmed — see "Encoders" below). Pad RGB lighting is implemented over a
> general SysEx transport: a curated starter palette plus a full arbitrary-RGB
> picker (hex entry and RGB sliders), both going through the same wire format,
> and both physically confirmed working, including several custom colors
> across more than one pad. A second, simultaneously-connected Fire is also
> recognized now (its WinMM name carries a leading-number prefix, confirmed
> against two real units). The OLED display is now also implemented and
> **physically confirmed, 2026-09-20**: a fixed border-and-corner diagnostic
> pattern, sent to a real Fire for the first time ever, rendered perfectly on
> the first attempt — clean border on all four edges, the corner marker in
> the correct top-left position, no flicker or glitching — see "OLED" below.
> Physical testing also surfaced a real, previously-undocumented hardware
> behavior: the Fire's OLED reverts to black on its own a few seconds after
> its last update (Tappy never sends a clear/off command), which is why a
> second "OLED test — all Fires…" action was built and physically confirmed
> to light two real Fires' screens together, back-to-back in code rather than
> one manual click per tab — see "OLED — sending to multiple Fires at once"
> below. The OLED can now also show real content instead of just the
> diagnostic pattern, via a per-tab "OLED display:" mode selector (Off / Live
> status / Tappy logo / Scrolling TerkWerX), all **physically confirmed,
> 2026-09-20** — live status (device name + active layer) stays lit
> indefinitely via periodic re-send and updates within one refresh after a
> layer switch; the Tappy logo is a hand-and-T silhouette bitmap converted
> from the app's own icon; the scrolling wordmark is "TerkWerX" in a large
> hollow outlined font filling the OLED's full height, scrolling left. A
> confirmed Fire also automatically plays a one-time welcome sequence (logo,
> then the scrolling wordmark, then whatever mode was selected) the first
> time it is confirmed each session, whether from a fresh identification or
> a silent startup reconnect — see "Live status display" below. The Fire's
> Modes/Bank indicator LED is also now implemented and physically confirmed:
> it visibly follows Tappy's own active bank whenever a mapped physical
> button changes it, and the Fire's own bank count was raised from 3 to 4 to
> match its hardware LED's four positions. Button/rectangular LEDs remain
> researched but NOT yet implemented. The device name,
> USB identity, and photo are confirmed, and the owner has physically
> pressed/turned every button, pad, and encoder through Tappy's generic MIDI
> learn path (before the encoders were added to the catalog) — see
> "Physically confirmed" below. That same pad-color test surfaced a real UI
> bug (a color action could silently recolor an unrelated pad when an
> explicitly checked group turned out to be non-addressable) and a layout
> confusion (encoders mixed into the 64-pad grid); both are now fixed —
> encoders sit on their own row above the pads, and the fallback bug is
> corrected. A separate, longer-lived bug then surfaced on the owner's
> already-in-use profile: a Fire confirmed before that encoder/pad separation
> shipped never had its saved layout migrated to the new shape, so the
> encoders kept reappearing jumbled into the pad grid on every later launch
> regardless of which build was running. Fixed by detecting a saved layout's
> outdated shape and resetting its positions instead of reusing them — see
> D-022 in DECISIONS.md. Recognizing the second Fire's input also exposed a
> matching output-side gap (lighting sync picked any Fire-shaped output port,
> ambiguous with two connected); fixed by matching the confirmed device's own
> exact port name — see D-024. All three fixes are physically confirmed
> against two real Fire units on TESTBED, 2026-09-20.

## Exact model and evidence

Tappy targets the original Akai Professional **Fire** ("FIREXUS Fire Performance
Controller for FL Studio"), a class-compliant USB-MIDI device bundled with/for FL
Studio. Research as of 2026-09-19 found no "Fire mk2" or other hardware revision;
this is the only shipping Fire model.

**Confirmed by direct read-only enumeration on TESTBED, 2026-09-19** (once the
owner connected a real Fire): Windows reports USB `VID_09E8`/`PID_0043` (Akai
Professional; `0043` also matches the Fire's own SysEx product byte below — an
independent cross-check), and WinMM reports the exact port name `FL STUDIO FIRE`
for both its MIDI input and MIDI output. `MidiControllerModelCatalog` and
`ControllerPhotoCatalog` match on that exact confirmed string; no correction is
needed. Only one input port and one output port were present for the single
connected unit. A second simultaneous Fire is expected to need its own
disambiguation (see the multiple-Fire note near the end of this document) but is
not implemented or tested here.

Akai has not published a MIDI implementation chart or SysEx specification for the
Fire; their own support content states the device is meant to be used through the
FL Studio control-surface script, not as a documented generic MIDI controller. All
protocol-level facts below are community reverse-engineering, not manufacturer
documentation, and are labeled accordingly.

### Sources

- Manufacturer-documented physical/functional facts: the official
  [AKAI PROFESSIONAL FIREXUS Fire Performance Controller for FL Studio User Guide](https://www.adorama.com/col/productManuals/AKFIRENS.pdf)
  (numbered top-panel/rear-panel diagram and control descriptions) and
  [Akai's Fire FAQ](https://support.akaipro.com/en/support/solutions/articles/69000800915-akai-pro-fire-frequently-asked-questions).
- Community-reverse-engineered protocol: SEGGER's three-part
  ["Decoding the Akai Fire" series](https://blog.segger.com/decoding-the-akai-fire-part-1/)
  ([part 2](https://blog.segger.com/decoding-the-akai-fire-part-2/),
  [part 3](https://blog.segger.com/decoding-the-akai-fire-part-3/)) by Paul Curtis —
  the primary technical source, with the article's own confirmed/tested vs.
  inferred distinctions preserved below. Independently corroborated (without being
  copied — see licensing note) by multiple maintained open-source projects that
  reverse-engineered or reused the same protocol:
  [jamisonx-dev/akai-fire-handler](https://github.com/jamisonx-dev/akai-fire-handler)
  (MIT), [scjurgen/AkaiFireMapper](https://github.com/scjurgen/AkaiFireMapper) (MIT),
  [Reg0lino/AKAI-Fire-RGB-Controller](https://github.com/Reg0lino/AKAI-Fire-RGB-Controller),
  [Vykonn/akaifire.py](https://github.com/Vykonn/akaifire.py),
  [nfxbeats/FIRE-NFX-V2](https://github.com/nfxbeats/FIRE-NFX-V2), and
  [DrivenByMoss's FireControlSurface/FireDisplay](https://github.com/git-moss/DrivenByMoss/blob/master/src/main/java/de/mossgrabers/controller/akai/fire/controller/FireControlSurface.java)
  (LGPLv3). No implementation code has been copied from any of these.
- A second, independent cross-check: the owner has a separate, unrelated personal
  project (not Tappy or Tippy) that already implements Fire button/pad/encoder
  input and RGB/OLED output as its own original code, informed by the same public
  sources above but not copied from them. Its note/CC table matches every button
  and pad number in this document exactly, its relative-encoder decode formula is
  reused directly below, and its own SysEx packet builders match this document's
  pad-color and OLED formats byte-for-byte where compared. Reusing that project's
  code here carries no licensing concern — it is the same owner's own original
  work — but its own docs record that its LED/OLED output, specifically, was not
  yet physically verified either as of its last update; it is strong corroborating
  software evidence, not a hardware-verified source.

## Physical control inventory (manufacturer-documented)

From the official user guide's numbered top-panel diagram:

- **Modes** button cycling Channel Rack / Mixer / User 1 / User 2, with four
  indicator LEDs.
- **Display**: OLED, top center.
- Four knobs: **Volume**, **Pan**, **Filter/Low EQ**, **Resonance/High EQ** — the
  last two carry a second (Shift) legend; all four are reused across the four Modes
  above (one physical row of four knobs, four logical banks).
- **Pattern ▲/▼** buttons.
- **Browser** button.
- **Select** knob (rotate to scroll a channel/track; press/hold to activate it).
- **Grid** button with left/right legends.
- Four **Mute/Solo** buttons, one per pad row.
- **64 pad buttons**, arranged 4 rows × 16 columns.
- **Step/Accent**, **Note/Snap**, **Drum/Tap**, **Perform/Overview**, **Shift**,
  **Alt** buttons.
- **Pattern Song/Metronome**, **Play/Wait**, **Stop/Countdown**,
  **Record/Loop Record** buttons.
- Rear panel: one USB port only (bus-powered, class-compliant, no driver install)
  and a Kensington lock slot. No 5-pin DIN MIDI in/out.

Akai's FAQ states pad velocity sensitivity is "limited... to allow accented notes,"
not a smooth 127-level response — SEGGER's independent testing agrees the pads are
"very, very hard to control" for anything but on/off-style presses. Tappy must not
present the Fire as offering fine-grained velocity.

**Important:** Shift and Alt are ordinary buttons from Tappy's perspective, not
hardware modifiers. On real USB-MIDI wire traffic each control has one fixed note
number regardless of whether Shift/Alt is held; the printed secondary (Shift)
legends are interpreted only by FL Studio's own control-surface script, which Tappy
does not run. Every primary and secondary legend is therefore exposed as one
ordinary independently assignable control, exactly like Tappy's existing controllers.

## Input protocol (community-reverse-engineered, SEGGER part 1 — confirmed by testing per that article)

All input is Note On/Off (status `90`/`80`) or Control Change (`B0`) on **MIDI
channel 1**.

### Buttons (implemented in Phase 1a)

| Control | Note (hex) |
|---|---|
| Modes/Bank | `1A` |
| Pattern ▼ | `1F` |
| Pattern ▲ | `20` |
| Browser | `21` |
| Grid Left | `22` |
| Grid Right | `23` |
| Mute/Solo 1–4 | `24`–`27` |
| Step/Accent | `2C` |
| Note/Snap | `2D` |
| Drum/Tap | `2E` |
| Perform/Overview | `2F` |
| Shift | `30` |
| Alt | `31` |
| Pattern Song/Metronome | `32` |
| Play/Wait | `33` |
| Stop/Countdown | `34` |
| Record/Loop Record | `35` |

The exact up/down assignment of the two Pattern-navigation notes (`1F`/`20`) and
the physical top-to-bottom order of the four Mute/Solo notes (`24`–`27`) are the
article's examples, not independently re-derived by Tappy; a mislabeled direction
would show the wrong on-screen name for a still-fully-functional assignable
control, so it is deferred to attended physical confirmation rather than blocking
this phase.

### Pads (implemented in Phase 1a)

64 pads, 4 rows × 16 columns, contiguous notes `36`–`75` (row 1 = `36`–`45`,
row 2 = `46`–`55`, row 3 = `56`–`65`, row 4 = `66`–`75`). Which physical row is
"row 1" (top vs. bottom) is not stated in the source and needs attended
confirmation; Tappy's layout currently assumes row 1 is the top row, matching the
reading order of the official diagram.

### Encoders (implemented)

Four touch-capacitive endless encoders (Volume/Pan/Filter/Resonance) plus a fifth
touch-capacitive Select encoder:

| Control | Touch note (hex) | Rotation CC (hex) |
|---|---|---|
| Volume | `10` | `10` |
| Pan | `11` | `11` |
| Filter | `12` | `12` |
| Resonance | `13` | `13` |
| Select | `19` | `76` |

Touch is an ordinary Note On (press) / Note Off (release), independent of rotation
— Akai's own marketing and the FAQ agree these five knobs are touch-capacitive, and
SEGGER's testing confirms the note numbers. This is genuine touch capability, not
an assumption.

**Physically confirmed, 2026-09-19:** with only buttons and pads in the catalog,
the owner touched/turned the four main encoders and the Select encoder on the
physical unit. Tappy's generic "learn an unrecognized control" path (the same
fallback every unclassified MIDI control gets) picked up and displayed exactly
notes `10`–`13` and CC `10`–`13`, plus CC `76`, live in the running app —
independently corroborating the touch-note and rotation-CC numbers above against
real hardware, not just documentation. That same observation also surfaced the
bug described next in person: the CC events appeared as separate "increase" and
"decrease" tiles produced by Tappy's *absolute*-diff decoding, which is the wrong
decoder for these controls (see below) — the owner was looking at the bug's output
directly, not a hypothetical.

Rotation is **relative**, not absolute: each detent sends one Control Change whose
value is a signed delta (clockwise vs. counterclockwise), not a position to compare
against the previous reading. Tappy's original MIDI Control Change decoding
(`WinMmMidiInputProvider`) only understood *absolute* CC values — it inferred
direction by diffing against the previously received value, which is how the APC
MINI's faders are modeled, and produced misleading direction/magnitude for the
Fire's encoders if reused as-is (confirmed above). `WinMmMidiInputProvider` now
also supports relative/signed CC decoding as a general capability
(`SetRelativeControlChanges`, keyed by CC number only — not Fire-specific), and
`MidiControllerModelCatalog`'s `AkaiFireV1` declares CC `16, 17, 18, 19, 118` (the
five rotation numbers) as relative. The decode function (independently confirmed
against the cross-check project's own implementation):

```csharp
// 1-63 = clockwise steps of that size; 65-127 = counterclockwise (value-128);
// 0 and 64 are the encoder's neutral/no-movement values.
private static int DecodeRelativeControlChange(int value) => value switch
{
    >= 1 and <= 63 => value,
    >= 65 and <= 127 => value - 128,
    _ => 0,
};
```

Each encoder is three assignable controls: one touch button (ordinary Note
On/Off) and two rotation directions (`cc-N:increase`/`cc-N:decrease`, reusing the
exact same ControlId scheme as the APC MINI's faders), whose event carries the
real step magnitude (not a fixed ±1) via `RuntimeControlUpdate.AnalogDelta`,
wired through the existing `LayoutControlKind.Encoder` / `ApplyEncoderDelta`
visual-encoder plumbing in `ProfileModels.cs` and `ControlTileViewModel.cs` (built
earlier, previously unused by any model). That visual rotation feedback only
activates for a control with a photo hotspot of `ControllerPhotoControlKind.RotaryEncoder`
— **implemented 2026-09-20, not yet physically confirmed:**
`ControllerPhotoCatalog`'s Fire photo now has a measured circular hotspot for
all five knobs, so every one of the 99 model controls (buttons, pads, and all
15 encoder controls) has a photo hotspot. Coordinates were found by the same
column-brightness-peak technique already used for the pad grid, applied to
the `VOLUME`/`PAN`/`LOW EQ`/`HIGH EQ` header text that sits directly above
each of the four main knobs (reliable, high-contrast white-on-black text,
unlike the knobs' own low-contrast metal gradient), then confirmed by drawing
a candidate circle over the photo and visually checking it against the actual
knob for all five, including Select (whose position could not be derived from
the header-text technique and was found by direct visual inspection instead).
Touch, clockwise, and counter-clockwise share one visual spot per knob via a
common `VisualId`, the same alias pattern the Targus NumLock/Enter hotspots
already use elsewhere in `ControllerPhotoCatalog`. Selecting an encoder's
rotate tile in the running app now visibly highlights the correct knob on the
photo (confirmed on Volume and Select — the first and most differently-derived
of the five — via screenshots of the live app); turning a real encoder to
confirm the rotating indicator itself remains a physical check. `AnalogRawValue`
(an absolute position readout) is deliberately never set for these controls,
matching the "must not be presented as having a known absolute position" rule
for relative endless encoders — the rotary indicator's angle comes entirely
from `ApplyEncoderDelta`, not a calibrated min/max sweep.

## Output/lighting protocol

SEGGER part 2 (button/rectangle/bank LEDs) and part 3 (pad RGB LEDs, OLED), each
explicitly marked in that source as confirmed by testing:

- **Button LEDs**: Control Change with the *same* controller number as the
  button's input note (e.g. Play LED is `B0 33 xx`). Each button group supports
  only the colors silk-screened on it (red-only, green-only, yellow-only, or a
  dual-color pair), with a small enumerated intensity range (off/dull/high per
  color) — not arbitrary RGB. Four rectangular bi-color LEDs use CC `28`–`2B`.
  Their physical position — immediately beside the four Mute/Solo buttons, one
  per pad row — is now confirmed by direct inspection of the real unit (see
  item 3 below and D-035 in DECISIONS.md); **which CC value drives which of
  the four, and what they indicate, remains unconfirmed** — nothing in Tappy
  currently sends CC `28`–`2B`, so this is an open question for a future
  physical test, not yet an implemented or verified feature. Master
  all-on/all-off is CC `7F`.

  **Modes/Bank indicator (implemented and physically confirmed, 2026-09-20):**
  CC `1B` (`27` decimal), channel 1, value = the 0-based active bank index
  (`0`–`3`). This resolves what was previously an open discrepancy between two
  unverified readings — the cross-check project's own implementation sending
  the plain bank index with `16` as "all off," versus this document's earlier,
  separately-sourced reading of a `0x10`-based bitmask, neither confirmed
  against real hardware at the time either was written. The plain-index
  reading is the one that turned out correct: `DeviceAwareControllerRuntime.SyncFireBankIndicatorLocked`
  sends it automatically whenever `ChangeBank` changes a confirmed Fire's
  active bank (e.g. a physical button assigned to "next bank"), so the
  Fire's own four-position hardware LED visibly agrees with Tappy's own
  active bank. Best-effort — a failed sync (no real output wired, output
  temporarily unavailable) never blocks the bank change itself, matching
  every other Fire lighting sync's fail-soft precedent. **Physically
  confirmed, 2026-09-20:** the owner assigned the Fire's own physical
  Modes/Bank button to Tappy's "next bank" control and confirmed pressing it
  both changes Tappy's active bank and moves the physical LED to match, on
  the first attempt with the plain-index encoding. See D-030 in
  DECISIONS.md.
- **Pad LEDs (implemented, not yet physically verified)**: true RGB, but only
  reachable via a System Exclusive message — ordinary Note On does not illuminate
  a pad. Format: `F0 47 7F 43 65 <lenHi> <lenLo> [index red green blue]... F7`,
  manufacturer ID `47` (Akai — independently confirmed against the standard MIDI
  Manufacturer ID registry, not only this source), Sub-ID `65` = write pad array,
  pad index `00`–`3F` = row×16+column, each color channel `00`–`7F` (an 8-bit
  `0`–`255` input halved per channel with `channel >> 1` for MIDI's 7-bit limit,
  matching the cross-check project's own encoder). `DeviceAwareControllerRuntime.FirePadColorSysEx`
  batches every changed pad into one message using this exact format (covered by
  deterministic byte-level tests), reusing the same "Color selected"/"Sync
  supported LEDs" UI every other lighting-capable controller already has. Colors
  offer a **curated starter palette** (`AkaiFireColorKeys`: Red, Amber, Orange,
  Yellow, Green, Teal, Blue, Purple, Pink, Gray, White, Default/off) for quick
  selection, plus a **full RGB picker** (`FireColorPickerWindow`: hex entry and
  R/G/B sliders) for any color, gated behind the new
  `ControllerLedColorCapability.SupportsFreeformColor` flag (only the Fire sets
  it). A picked custom color is stored as a `"#RRGGBB"` string in the same
  `ColorKey` field the named palette already used — `DeviceAwareControllerRuntime.FireRgb`
  tries to parse that shape first and only falls back to the named-color switch
  when it does not match, so both paths share one SysEx encoder and one on-wire
  format. The starter palette was deliberately built and verified first, to get
  the riskiest new piece (the SysEx transport itself) proven against real
  hardware with a small, easy-to-inspect test before adding picker UI; the picker
  reuses that same proven transport unchanged; see the note under "Physically
  confirmed" below about what specifically has and has not been tried on hardware.

  **Physically confirmed, 2026-09-19:** the owner selected one pad, applied
  green via "Color selected" then "Sync supported LEDs," and the physical pad
  lit green; selecting three pads and applying blue lit all three (exercising
  the batched multi-pad SysEx path) — both exactly as expected, the first real
  SysEx transactions this transport has ever sent. A third attempt — selecting
  7 tiles that turned out to include encoder touch/rotation controls, which
  are not pad-addressable — applied no color, then on a repeated attempt
  unexpectedly recolored one unrelated pad. This was not a transport or SysEx
  bug: `MainViewModel.ApplySelectedTileColorAsync` fell back to whatever
  single tile last held individual UI focus whenever a color action found
  zero addressable targets, even when the user had explicitly checked a whole
  group that simply had no addressable members. Fixed so the fallback only
  applies when nothing at all is checked; an explicitly checked all-non-
  addressable group now leaves hardware untouched and reports "None of the N
  selected squares are color-addressable on this controller." The encoders
  were also moved out of the 64-pad grid entirely (their own row, above the
  button strip) specifically so they can no longer be visually confused with
  color-addressable pads during selection.

  **Physically confirmed, 2026-09-20:** the full RGB picker (arbitrary hex/
  slider colors) is implemented and covered by deterministic tests (hex
  parsing, malformed-input rejection, the picker surviving an unrelated
  runtime event without losing an armed-but-unapplied selection — the same
  "stomping" class of bug fixed for per-tab zoom, see D-019 in DECISIONS.md).
  The owner then tried it on the physical unit: several custom colors across
  more than one pad, applied and synced through the picker, each matching the
  physical pad's actual lit color. This is an attended visual spot check, not
  an instrumented per-color capture or exhaustive coverage of the 24-bit
  space, but it directly exercises the same code path as every other color a
  user might pick, unlike the earlier starter-palette check which only proved
  eleven specific values.
- **OLED (implemented and physically confirmed, 2026-09-20)**: 128×64
  monochrome (8,192 pixels), also SysEx-only:
  `F0 47 7F 43 0E <lenHi> <lenLo> [00 07 00 7F][1,171 packed bytes] F7`, Sub-ID
  `0E` = write OLED. The pixel-to-byte/bit packing is not a simple linear
  framebuffer — SEGGER found "no obvious logic" after exhaustive experimentation.
  The cross-check project's independent implementation expresses that same
  non-linear mapping as a closed-form formula rather than a lookup table (`x`,`y`
  are pixel coordinates in the 128×64 bitmap; `packet[11..]` is the payload after
  the four header bytes above):

  ```csharp
  var column = x + ScreenWidth * (y / 8);
  var tileColumn = column % 7;
  var row = y % 8;
  var bit = 6 * tileColumn + row + (row <= tileColumn ? 13 : -1);
  var index = column / 7 * 8 + bit / 7;
  packet[11 + index] |= (byte)(1 << (bit % 7));
  ```

  This is treated as the authoritative reverse-engineering result here, not
  re-derived independently, and credited to SEGGER's published findings.
  `DeviceAwareControllerRuntime.FireOledFrameSysEx`/`FireOledBitPosition`
  implement this formula verbatim; `FireOledFrameSysEx_matches_the_documented_*`
  and `FireOledBitPosition_maps_every_pixel_to_a_unique_in_bounds_bit`
  (`FireOledTests.cs`) verify the implementation is internally consistent — every
  one of the 8,192 real pixels lands on its own distinct in-bounds bit and the
  message framing matches the documented byte layout exactly — without re-
  deriving or re-validating the formula's own correctness against real hardware,
  which only a physical test can do.

  This is a genuinely new, previously-untried SysEx sub-command: unlike pad
  colors (proven below), Sub-ID `0E` has never been sent to a real Fire by
  Tappy. Exposed behind a new "OLED test (experimental)…" button (visible only
  once a Fire is confirmed), which sends a single fixed diagnostic pattern —
  `DeviceAwareControllerRuntime.FireOledDiagnosticPattern`: a full one-pixel
  border plus a solid 8×8 square in the top-left corner only, so a scrambled
  packing bug (noisy border) and an orientation/mirroring bug (marker in the
  wrong corner) are both immediately visually obvious — deliberately not a
  general drawing API or text renderer yet, to isolate the packing formula's
  own correctness from any font-rendering risk on the very first real write.
  The button requires an explicit Yes/No confirmation naming the destabilization
  risk noted below before sending anything.

  **Physically confirmed, 2026-09-20:** with the owner watching the physical
  screen, the diagnostic pattern was sent to a real Fire for the first time —
  it rendered perfectly on the very first attempt: a clean one-pixel border
  on all four edges, the solid corner marker in the correct top-left position,
  no flicker, no glitching, no hang, no disconnect. This confirms the packing
  formula, the fixed message framing, and this transport's first real use at
  the OLED frame's much larger size (1,183 bytes, versus a few dozen for pad
  colors) all in one attended check. Only the one fixed diagnostic bitmap has
  been tried — arbitrary content (text, status display, etc.) is unimplemented
  and untested.

  **New hardware behavior discovered, 2026-09-20 (undocumented by any source
  above until now):** the Fire's OLED does not hold a written frame
  indefinitely. After the first single-unit send, the owner reported the
  pattern appeared correctly, then went dark on its own after "a few seconds,
  not minutes" — nothing in Tappy sends a clear/off command, so this is the
  Fire's own firmware idle-reverting the display after it stops receiving
  updates (consistent with the OLED protocol being designed for FL Studio's
  control-surface script, which presumably refreshes it continuously). This
  means any future real content (status text, etc.) built on this transport
  will need to be re-sent periodically to stay visible, the same way LED
  animations already re-tick — a one-shot write is only ever temporary. See
  "OLED — sending to multiple Fires at once" below for the multi-device
  consequence of this finding and its own physical confirmation.

- **OLED — sending to multiple Fires at once (implemented and physically
  confirmed, 2026-09-20):** `ControllerWorkspace.SendOledTestPatternToAllFires`
  sends the same diagnostic pattern to every confirmed Fire tab, one right
  after another with no UI round-trip in between (unlike clicking the
  single-device button once per tab, which has enough delay between clicks
  for the first unit's pattern to already have reverted before the second
  send goes out — confirmed directly: the owner first tried it by switching
  tabs and clicking each tab's own button in turn, and saw each unit's
  pattern appear and then disappear before the other's appeared). Exposed as
  a second button, "OLED test — all Fires…", next to the per-tab one. This
  does not implement anything about the shared-canvas participant/offset
  model `LED_ANIMATIONS.md`/D-025 describe for pad-LED animations — it sends
  the identical fixed bitmap to every unit, not a canvas split across them;
  a real multi-screen split, if wanted later, is unbuilt.

  **Physically confirmed, 2026-09-20:** with two real Fires connected and the
  owner watching both physical screens, both units lit up with the correct
  pattern at the same time, and both went dark together a few seconds later —
  confirming the back-to-back send lands inside the idle-revert window
  discovered above, and that neither unit's write interferes with the
  other's.

- **Live status display, Tappy logo, and scrolling wordmark (implemented and
  physically confirmed, 2026-09-20):** the OLED can now show real content,
  not just the diagnostic pattern, via a per-tab `FireOledDisplayMode`
  selector (Off / Live status / Tappy logo / Scrolling TerkWerX — a "OLED
  display:" button group next to Rehearsal Mode, default Off). A new,
  original 5x7 monochrome bitmap font (`Tappy.Core.Models.FireOledFont` —
  uppercase, lowercase, digits, space, and `. : - / ( ) '`, hand-authored as
  readable ASCII-art source rather than opaque byte tables) and a pure
  layout function (`FireOledTextRenderer.Render`) render plain text onto the
  pixel buffer for Live status (device name + active layer, both
  already-existing `MainViewModel` properties); an unsupported character
  renders blank rather than throwing, since layer names are user-editable
  free text. `Tappy.Core.Models.FireOledLogo` is a packed 128x64 bitmap — the
  app's own icon (a hand silhouette with a "T" mark) converted by
  compositing onto white, resizing, and isolating the mark by hue (blue T vs.
  warm skin tones) rather than a single luminance threshold, which would
  have buried the T inside the hand's shading. `FireOledTextRenderer.RenderScaledLine`
  renders one line at an arbitrary integer pixel scale for the Scrolling
  TerkWerX mode (scale 9 — the largest whose 7-row glyph height still fits
  the OLED's 64px height, "fat letters filling the screen top to bottom" per
  the owner's request), and `FireOledTextRenderer.Outline` (3px stroke)
  hollows it out per a follow-up request ("can't you make the letters
  hollow?" / "there should be enough pixels to make it fat"), rather than a
  solid block. `FireOledScroll.ExtractWindow` extracts a wrapping,
  gap-separated window from that wide buffer as a pure function of an offset
  the caller tracks over time — the same "elapsed time in, colors out" shape
  `LedEffectMath` already uses for pad animations. A new, generalized
  `DeviceAwareControllerRuntime.SendFireOledFrame(bool[,] pixels)` sends any
  such buffer — `SendFireOledDiagnosticPattern()` is now a one-line wrapper
  over it, unchanged in behavior. Before any hardware use, every font glyph,
  the full logo bitmap, and the scaled/hollow scrolling text were rendered
  at true 128x64 scale via the Artifact/visualize tool and visually
  confirmed correct.

  Because the OLED reverts to black on its own a few seconds after its last
  update (see above), showing anything persistently needs a re-send loop,
  not a single render — the same reason LED pad animations re-tick
  continuously. A new per-tab `FireOledContentPlayer`, ticked every 150ms by
  a new `_oledContentTimer` (mirroring `_animationTimer`'s ownership shape;
  150ms — not the original 2s used for the live-status-only prototype — both
  because a static mode does not need updates that often and because the
  scrolling wordmark needs faster updates to look smooth), re-reads
  `MainViewModel.OledDisplayMode` and re-renders/re-sends every tick. This is
  opt-in, not automatic for a *manually selected* mode: it is the first
  continuous, unattended, repeating SysEx write pattern anywhere in Tappy —
  every other Fire lighting/OLED action so far has only ever sent on one
  deliberate click.

  **Welcome sequence:** the owner asked for the logo and scrolling wordmark
  to play automatically "when the program is opened or a Fire controller is
  plugged in and recognized... for the first time in a session." `FireOledContentPlayer`
  tracks, per tab, whether a Fire has ever become confirmed this running
  session (covering both a fresh manual identification and a silent startup
  reconnect — both funnel through the same "just became confirmed" state
  transition) and, the first time only, plays the logo for 3s then the
  scrolling wordmark for 6s before falling through to whatever mode is
  selected (default Off). Two real bugs surfaced physically testing this and
  were fixed the same session: a disconnect mid-welcome left its clock
  running, so a quick reconnect resumed a stale, partially-elapsed welcome
  instead of treating it as already played (fixed by clearing the welcome
  clock on disconnect — caught by a regression test, confirmed to fail
  without the fix); and the scroll's speed (initially tuned by feel) was too
  slow to complete one pass of "TerkWerX" inside the welcome's 6s scroll
  phase, then, once sped up, could end mid-way into a *second* partial
  repeat depending on which 150ms tick the phase happened to end on — fixed
  by clamping the welcome's scroll-relative elapsed time so it settles on a
  clean blank screen once one full pass completes, rather than continuing to
  compute an offset that could land anywhere in a second repeat.

  **Physically confirmed, 2026-09-20,** owner watching both real Fires
  throughout, across several iterations: a single confirmed Fire's device
  name and active layer rendered correctly with no glyph errors and stayed
  lit for over 40 seconds (past the hardware's own few-second revert
  window); switching the active layer updated the second line within one
  refresh tick; turning the mode to Off was followed by the screen going
  dark on its own within about 10 seconds (Tappy sends no explicit clear —
  the hardware's own idle-revert takes over); both Fires showed independent
  correct content simultaneously; the Tappy logo rendered correctly; the
  scrolling wordmark, after the speed and end-of-cycle fixes, scrolled the
  fat hollow "TerkWerX" across the screen once smoothly and settled cleanly
  on blank; and the welcome sequence played correctly on both Fires at
  startup reconnect, in the corrected order (logo, then scroll, then
  settling), with no replay on a subsequent reconnect. Only device
  name/active layer, the fixed logo, and the fixed "TerkWerX" wordmark are
  implemented; custom static text and other live content (e.g. a
  rehearsal-mode indicator) remain unimplemented. See D-029 in
  DECISIONS.md.

**SysEx transport (implemented, physically confirmed 2026-09-19/20 for pad
colors and 2026-09-20 for the larger OLED frame — see "Physically confirmed"
above and in "OLED" above):** `WinMmMidiOutput` could
previously only send short (3-byte) MIDI messages (`midiOutShortMsg`). It now
also has `SendSysEx`, backed by a new `IWinMmMidiOutputBackend`/
`WinMmMidiOutputBackend` pair (mirroring the existing testable-backend pattern
already used on the MIDI input side) that correctly uses
`midiOutPrepareHeader` / `midiOutLongMsg` / `midiOutUnprepareHeader` with a
`CALLBACK_EVENT` handle, waiting for Windows to confirm delivery (`MHDR_DONE`)
before freeing the native buffer. This exists specifically because the
cross-check project's own docs flag that NAudio 2.2.1's `MidiOut.SendBuffer`
frees its native buffer immediately after submitting the long message, which is
unsafe — the driver can still be reading it. If a send times out or reports an
inconsistent completion state, that one buffer is deliberately leaked (bounded,
rare) rather than freed while its ownership is unknown, and the failure is thrown,
not swallowed. Covered by 10 deterministic tests using a fake backend (device
resolution, reopen-on-device-change, F0/F7 framing validation, failure → close →
reopen). The real native prepare/send/wait/unprepare sequence itself has since
been exercised against real hardware for both pad-color messages (up to a few
dozen bytes) and the much larger single-message OLED frame (1,183 bytes
total), and both worked correctly — the message-size question this transport
carried since its own write-up is now closed for the sizes Tappy actually
sends. SysEx values outside the tested range are still reported to be able to
destabilize the device in general, so that standing caveat remains for any
future, differently-shaped SysEx message, just not for what is implemented
today.

## What is implemented now

`MidiControllerModelCatalog` recognizes the confirmed exact WinMM MIDI port name
`FL STUDIO FIRE` and immediately publishes a fixed 99-control surface (20 buttons
+ 64 pads + 5 encoders × 3 controls each) on confirmation, the same pattern used
for the APC MINI, with every default tile position mirroring the control's
physical location. The 5 encoders occupy their own row (row 0), entirely
separate from the 64-pad grid, so they cannot be visually mistaken for pads
during selection; each encoder's three controls are ordered
increase–touch–decrease, touch in the middle. Row 1 is the top button strip
(Modes/Bank, Pattern ▼/▲, Browser, Grid Left/Right). Rows 2–5 hold the
Mute/Solo strip (column 0), a deliberately empty gap column (column 1)
separating it from the pad grid, and the 64-pad grid itself (columns 2–17, in
physical reading order). Row 6 is the bottom button strip (mode buttons at the
left, transport buttons at the right). See the code comment on
`CreateAkaiFireV1` for the exact scheme. `ControllerPhotoCatalog` has a
matching photo locator for the
20 buttons and 64 pads (not yet the encoders): the owner supplied a product photo
(`docs/images/Akai Professional Fire controller.webp`, preserved untouched); the
runtime asset (`src/Tappy.App/Assets/Controllers/akai-fire-neutral.png`) is a
processed derivative with its background removed and the 64 pads plus every
colored button neutralized to an unlit dark-gray look, matching Tappy's
convention for other RGB-capable controllers. All 84 hotspot rectangles were
measured directly against that processed photo — the regular 64-pad grid by
automated column/row brightness-peak detection, the 20 irregularly-placed buttons
by visual measurement — and are covered by automated bounds/non-overlap/
completeness tests. Hotspot placement has not been physically compared against
the real hardware. Pad RGB lighting (starter palette and full RGB picker) is
implemented over the new SysEx transport and physically verified, including
several arbitrary custom colors across more than one pad (see "Physically
confirmed" under Pad LEDs above) — a larger batch and connection-stability
checks are still pending. The OLED display is implemented and physically
confirmed with a selectable mode per tab: the diagnostic test pattern, a
live status display (device name + active layer), the Tappy logo, and a
large hollow scrolling "TerkWerX" wordmark, each shown on one or both
confirmed Fires at once, plus a one-time welcome sequence (logo then
scrolling wordmark) the first time a Fire is confirmed each session (see
"OLED" and "Live status display, Tappy logo, and scrolling wordmark"
above). Custom static text and other live content (e.g. a rehearsal-mode
indicator) remain unimplemented. The Fire's own Modes/Bank indicator LED is
now implemented and physically confirmed, following Tappy's active bank
(now 4 banks by default, matching the LED's four positions — see "Modes/Bank
indicator" above); button and rectangular LEDs (CC `28`–`2B`) remain
unimplemented.

## Multiple Fires (implemented)

The cross-check project connected two Fires simultaneously and observed WinMM
naming the second unit with a **prefix**, `2 - FL STUDIO FIRE`, not the `(device N)`
**suffix** Tappy's own duplicate-device convention (established from the APC
MINI) expects. The owner then connected two real Fire units to TESTBED,
2026-09-20, independently confirming that exact naming (`MIDI: 2 - FL STUDIO
FIRE`) against Tappy's own enumeration. `MidiControllerModelCatalog.Find` now
also strips a leading `"<digits> - "` prefix before matching, alongside the
existing suffix strip, so a second (or third, etc.) simultaneously-connected
Fire gets the full 99-control layout and photo like the first. See D-023 in
DECISIONS.md.

Recognizing the second unit's *input* immediately exposed a second, separate
gap on the *output* side: pad-color sync picked its destination MIDI port by
asking "is this any Fire's output," which now matches both units and made
Tappy refuse to light either one ("More than one Fire output port is
present..."). Fixed by matching the confirmed device's own exact WinMM port
name instead of just its model, so each tab's Sync sends to that same tab's
own physical unit. See D-024 in DECISIONS.md.

**Physically confirmed, 2026-09-20:** both fixes together — with two real
Fire units connected, each got its own confirmed tab with the full 99-control
layout and photo, and applying/syncing a different custom color on each
tab's "Sync supported LEDs" changed only that tab's own physical unit,
independently of the other.

## A stale saved layout does not survive a later shape change (fixed)

A Fire confirmed and saved before the encoder/pad-grid separation shipped kept
loading with its encoders visually interleaved back into the Mute/Solo and pad
rows on every later launch, even on builds that included the separation fix in
their code — because the saved layout's Id was never version-bumped when that
shape changed, `MergeCodeDefinedLayout` had no way to tell the saved profile
was stale, and its "preserve saved per-control order" strategy silently
scrambles the result whenever the canonical row/column shape itself has
changed since the profile was saved. Fixed by comparing the saved layout's Id
against the current canonical Id and, on a mismatch, resetting every
control's position to the fresh canonical layout instead of reusing the old
order — while still carrying forward per-control customization (pad colors,
calibration) matched by control identity, not position. `AkaiFireV1LayoutId`
was bumped so every existing saved Fire profile goes through this reset once.
See D-022 in DECISIONS.md.

**Physically confirmed, 2026-09-20:** after deploying the fix, the owner's
same previously-broken profile rendered with the encoders back on their own
row above Mute/Solo and the pad grid, and its previously-set pad colors were
still in place.

## Required finite validation

None of this has been physically exercised through Tappy. Before any promotion
past code-supported:

1. Confirm the device in Tappy with Rehearsal Mode on; verify all 99 squares and
   the photo (for the buttons/pads) appear immediately, before any physical press.
2. Press each of the 20 buttons and all 64 pads once; confirm exactly one expected
   square *and* the correct photo hotspot respond for each, and establish which
   physical row is pad row 1.
3. The hotspot-placement half of this item is now resolved — **but the
   reverse of what it originally guessed.** The owner confirmed by direct
   physical inspection that the round element is the actual Mute/Solo
   *button* (it is what illuminates when pressed) and the separate small
   rectangle beside it is a distinct bank-LED *indicator*, not the button
   itself — this checklist item's original wording had the two backwards.
   The photo hotspot was measured and moved onto the circular button
   (center x≈78, radius 23, matching the photo's visible button ring)
   instead of its earlier position at x=137, which sat over the rectangular
   indicator rather than the button. See D-035 in DECISIONS.md. The Pattern
   ▲/▼ and Mute/Solo 1–4 direction/order assumptions above still need
   confirming with the button pressed and released physically on the real
   unit.
4. ✅ **Done, 2026-09-19:** touch each encoder and confirm only its touch square
   responds; turn each encoder both directions at slow and fast speed and confirm
   the increase/decrease squares respond with a direction and event count that
   actually match the physical motion. The owner confirmed this directly — turning
   each encoder at different speeds showed the expected incrementing behavior in
   Tappy, the first attended check of the relative-CC decode math with real
   magnitudes, not just the note/CC numbers.
5. Test simultaneous pads, disconnect/reconnect, profile save/load, and input from
   an unselected controller, matching the existing controller test procedures.
6. ✅ **Done for the starter palette and the full RGB picker, 2026-09-20:**
   single-pad and 3-pad-batch "Color selected" → "Sync supported LEDs" lit the
   correct pad(s) with the correct color, no error, no hang (see "Physically
   confirmed" under Pad LEDs above). That check surfaced and led to fixing a
   color-fallback UI bug and an encoder/pad layout confusion, both corrected;
   a further attended check of the full RGB picker then surfaced the
   longer-lived stale-layout scramble (D-022) and the second-Fire naming gap
   (D-023), both also now fixed. Still needed: a larger batch (e.g. a full
   64-pad "Sync supported LEDs") and disconnect/reconnect or longer-running
   stability during/after a SysEx send — treat any hang, wrong color, wrong
   pad, or device freeze/disconnect as a stop-and-report finding, not
   something to retry past.
7. ✅ **Done, 2026-09-20:** sent the "OLED test (experimental)…" diagnostic
   pattern to a confirmed real Fire (development machine, owner watching the
   physical screen) — the very first real send of SysEx sub-ID `0E`. Result:
   clean one-pixel border on all four edges, the solid corner marker in the
   correct top-left position, no flicker, no glitching, no hang, no
   disconnect, on the first attempt. Only the one fixed diagnostic bitmap has
   been tried; arbitrary OLED content (text, status display, etc.) is still
   unimplemented and would need its own attended check when built.
8. ✅ **Done, 2026-09-20:** sent the OLED diagnostic pattern to both confirmed
   real Fires at once via "OLED test — all Fires…" (development machine,
   owner watching both physical screens). First attempt (manually clicking
   the single-device button once per tab) revealed the Fire's OLED reverts to
   black on its own a few seconds after its last update — the two units were
   never lit at the same time that way. The dedicated all-Fires action, which
   sends back-to-back with no UI round-trip between sends, was then tried and
   confirmed both screens lit with the correct pattern simultaneously,
   reverting together a few seconds later. See D-028 in DECISIONS.md.
9. ✅ **Done, 2026-09-20:** enabled "Show live status on OLED" on a confirmed
   real Fire and confirmed the device name and active layer render correctly;
   confirmed the display stays lit and correct for 40+ seconds via the
   periodic refresh (past the hardware's own few-second revert); switched
   the active layer and confirmed the second line updates within one
   refresh tick; unchecked the box and confirmed the screen goes dark on its
   own within ~10 seconds; enabled it on both confirmed Fires at once and
   confirmed both show correct, independent status simultaneously. See
   D-029 in DECISIONS.md. Only device name and active layer are shown;
   custom static text and other live content (e.g. a rehearsal-mode
   indicator) are unimplemented and would need their own attended check.
10. ✅ **Done, 2026-09-20:** the bank-indicator CC `1B` encoding discrepancy is
    resolved — plain 0-based bank index, physically confirmed (see "Modes/Bank
    indicator" above and D-030 in DECISIONS.md). Button and rectangular LEDs
    (CC `28`–`2B`) remain unimplemented; once implemented, and once encoder
    photo hotspots are added, they need their own attended verification
    before promotion.
