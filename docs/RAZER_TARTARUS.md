# Original Razer Tartarus (RZ07-0103)

## Exact model and evidence

The original Tartarus is USB `1532:0201`. TESTBED enumerated this device on
2026-09-18. The operator reports no Synapse installation and factory assignments.
This is not the Tartarus Chroma, V2, or Pro. Matching is by provider and exact USB
VID/PID, not a substring in the display name. Source input remains pass-through.

Razer's [original master guide](https://dl.razer.com/master-guides/Tartarus/TartarusOMG-ENG.pdf)
(printed pages 5–6, 10, 15, 17) identifies the three thumb-module lights as
**keymap indicators** and describes eight sets of assignments. They are not
programmable input buttons or per-key RGB lighting. The original keypad has green
backlighting. The [OpenRazer keyboard interface documentation](https://github.com/openrazer/openrazer/wiki/Using-the-keyboard-driver)
names yellow, green, and blue indicators (including a legacy `red` API name).
These API names did not establish physical channel order on TESTBED; the
operator's attended color observations below are authoritative for this mapping.

## Implemented in Tappy

- Owner-supplied photograph, byte-identical, with a tight render-time viewport.
  Black keys and green legends retain their photographed appearance.
- Fifteen numbered tiles in three rows of five, then a separate thumb cluster:
  Alt, Space, and four arrow directions. All 21 raw keyboard reports appear on
  confirmation; selecting a tile and physical input use the existing visual
  locator. Eight-way diagonal movement is represented by two cardinal reports,
  not invented independent diagonal controls.
- Factory mapping from the manual's diagram: Tab/Q/W/E/R;
  Caps Lock/A/S/D/F; Left Shift/Z/X/C/V; thumb Alt and Space, plus arrow keys.
  Synapse remapping would invalidate this photo-to-report assumption.
- Eight independent Tappy keymaps using existing profile layers. A picker beside
  the photo switches banks and saves the selection. Existing assignments remain
  untouched in the other banks. Switching is refused with a held input, during
  identification/disconnection, or while the session is suspended/locked.
- A stale assignment editor cannot save into another bank after a bank change.
- Existing known-layout positions/sizes are preserved. Legacy discovery-order
  grids migrate to physical order without deleting assignments. Unknown reports
  remain separate and are placed to the right without covering known controls.

## Indicator preview and opt-in hardware synchronization

The photo initially says **preview — physical LEDs not synchronized**.
Choose **Sync bank LEDs** to send the current Tappy bank pattern to this confirmed
Tartarus. **Follow bank changes** enables synchronization when selecting another
bank; it starts unchecked and turns off on failure or loss of confirmation.
Rehearsal Mode may remain checked for this explicit lighting test: macro outputs
stay suppressed. Merely confirming a controller does not write its indicators.

The preview represents Tappy's own active bank. The status only reports
**confirmed by device readback at last sync** after all three channels match;
it is not a continuous monitor of changes made by other software.
Tappy's bank legend is: 1 yellow; 2 green; 3 blue; 4 yellow+green;
5 yellow+blue; 6 green+blue; 7 all on; 8 all off. This legend is Tappy's encoding,
not a claim that the original software uses the same ordering.

After explicit operator approval, the HID interface diagnostic ran on TESTBED
with a process-only execution-policy override. No permanent policy, driver, or
key assignment was changed. Windows exposes these collections for `1532:0201`:

| USB interface | Usage page / usage | Input bytes | Output bytes | Feature bytes |
|---|---|---:|---:|---:|
| MI_00 | 1 / 6 (keyboard) | 9 | 2 | 0 |
| MI_01, collection 1 | 1 / 6 (keyboard) | 16 | 0 | 0 |
| MI_01, collection 2 | 12 / 1 (consumer) | 16 | 0 | 0 |
| MI_01, collection 3 | 1 / 128 (system) | 16 | 0 | 0 |
| MI_01, collections 4 and 5 | 1 / 0 | 16 | 0 | 0 |
| MI_02 | 1 / 2 (mouse) | 9 | 0 | 91 |

The subsequent attended protocol check established MI_02 as the working Windows
feature transport. Report ID zero prefixes a 90-byte payload, making 91 bytes.
Opening this mouse-class HID collection with zero requested access and shared
read/write permits feature reports without taking over its mouse input stream.
The exact confirmed Windows ContainerId must match Tappy's persisted keyboard
identity. Missing, ambiguous, wrong-model, or wrong-capability matches fail closed;
there is no vendor-wide or first-matching-device fallback.

The transport independently implements these interoperability facts from
[OpenRazer's profile-indicator commands](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c),
[common LED commands](https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c),
and [report layout](https://github.com/openrazer/openrazer/blob/master/driver/razercommon.h):
transaction `FF`, command class `03`, SET `00` / GET `80`, data size 3, storage
selector 1, channels `0C`/`0D`/`0E`, and binary on/off state. Each SET-feature
request is followed by a 30 ms settling interval and GET-feature response.
Success status, transaction, channel, state, and XOR checksum are validated.
Source implementations were not copied.
On the tested original unit, physical colors are **yellow=`0E`, green=`0C`,
blue=`0D`**. GET and SET use this explicit translation; Tappy's logical mask bits
remain yellow=1, green=2, blue=4, preserving bank numbers and saved assignments.

Synchronization reads the original states before changing anything, changes only
needed channels, and verifies the final states. A failed partial write attempts
restoration on the same open handle only while the confirmation/bank is still
valid; unverified restoration is explicitly reported. Disconnects or a changed
confirmation/bank invalidate the operation and its UI receipt. Commands run off
the UI thread. No RGB-key, backlight, firmware, or factory-assignment commands are
sent, and no replacement USB driver is installed.

## Verification

Deterministic tests cover exact-model rejection, complete/nonoverlapping layout,
photo cropping, selection and press highlights, simultaneous arrow components,
bank isolation, stale-editor refusal, held-key refusal, saving/reconfirmation,
disconnection cleanup, and saved-geometry migration. Headless WPF renders have
been inspected in dark, light, and high-contrast themes, including the new LED
controls. Added transport/runtime tests cover malformed replies, failed baselines,
partial-write restoration, identical-device targeting, opt-in behavior, and stale
results after bank changes or reconfirmation. The complete solution passed all
606 tests on 2026-09-18 before the operator's follow-checkbox report.

### Device-tab integration correction

The operator found that **Follow bank changes** immediately unchecked itself.
The standalone production constructor had the indicator transport, but
`ControllerSessionFactory` (used by the actual multi-device app) did not supply it.
The immediate sync therefore failed and correctly disarmed following, making the
checkbox appear unclickable. Both device-tab slots now receive the native
transport. Regression tests check the real factory wiring and the WPF checkbox's
click/binding/handler path, delayed acknowledgements, following a picker change,
manual disabling, and automatic disarming after a failed sync. Standalone protocol
success must not be reported as end-to-end app integration success.
The correction passed all 609 solution tests, including the new factory tests
that failed before the fix. The attended retest should change from the currently
green indicator to blue, rather than requesting the color already displayed.

The operator subsequently reported that all buttons and the photo highlights
seemed to work properly. This is an attended visual spot-check report, not an
instrumented per-control capture or proof of every diagonal combination.

The 2026-09-18 hardware probe read channel `0C` on / `0D` off / `0E` off, then
received valid acknowledgements and matching readback for each individual channel
and all-off. It restored and verified that original `0C`-only state. A separate
run of the production exact-container transport also confirmed `0C`-only.
Earlier notes called this yellow-only based on an incorrect color/channel
assumption; the operator subsequently established that `0C` is physically green.
Readback proved channel state, not the visible color name.

### Physical color-order correction

The operator tested all eight patterns after the tab-wiring fix and confirmed
that the follow checkbox now stays checked (following itself not yet exercised).
The old sequential channel assignment produced these visible results:

| Requested pattern | Observed physical indicators before correction |
|---|---|
| Yellow | Green |
| Green | Blue |
| Blue | Yellow |
| Yellow + green | Green + blue |
| Yellow + blue | Yellow + green |
| Green + blue | Yellow + blue |
| All three | All three |
| All off | All off |

This consistently identifies the same three-channel permutation. The transport
now uses the observed color/channel mapping for requests, readback, and rollback.
Dropdown labels, bank order, photo colors, and saved assignments are unchanged.
Tests independently model the physical channel colors: all six colored patterns
and the three explicit channel assertions failed before the correction, while
all-on/all-off still passed, reproducing the operator's observations.
After correction, all 612 solution tests passed, including all eight patterns,
read/write channel translation, and partial-write restoration.
The corrected mapping still needs an attended physical retest; do not infer that
readback alone verifies visible colors.

### Subsequent attended results and editor cleanup

The operator confirmed that all LED colors now match and that **Follow bank
changes** switches them from the dropdown without pressing Sync. They also
assigned button 01 to F24 in yellow/bank 1 and F22 in green/bank 2, switched back
and forth, and confirmed both assignments remained separate. This verifies
attended bank isolation in the current session, not actual emitted output or
restart/reconnect persistence.

The assignment editor's step controls now use three aligned two-button rows
with equal sizing and eight-DIP gaps. Footer actions share a consistent height.
An editor-specific item template keeps selected step text readable when the list
is unfocused in high-contrast mode. Nine layout cases cover three themes and
minimum/default/wide sizes, with 100/125/150% render scales; all 621 solution
tests passed. No assignment semantics changed in that editor-only build.

The subsequent cross-controller bank-control build adds physical next/previous/
direct bank controls, including another confirmed controller as the source.
See [Controller bank controls](CROSS_CONTROLLER_BANKS.md) for setup, preserved
underlying assignments, connection safeguards, and the attended checks still needed.

Still required on the physical unit: the updated app's attended LED/bank workflow,
instrumented key/direction verification as needed for the Passport, diagonals,
assignments in two banks, and reconnect.
These tests do not certify a driver or promote the controller to Verified.

### Multi-interface identity and startup reconnect

The original Tartarus exposes several keyboard-usage-page collections across two
USB interfaces (a composite device), not a single interface like most other
supported keyboard-class controllers. Windows reports each of those interfaces
arriving separately, even when the whole physical unit was never unplugged. This
surfaced as a real bug during the reconnect-across-restart investigation on
2026-09-19: the device's own second interface would "arrive" moments after Tappy
started, get classified as the same identity's membership changing, and (before
the fix) that would silently cancel the startup reconnect attempt every time,
requiring a manual re-identification on every launch despite the device never
having moved. See D-020 in [DECISIONS.md](DECISIONS.md) and
[REMEMBERED_RECONNECT.md](REMEMBERED_RECONNECT.md) for the fix. Single-interface
keyboard-class controllers (Targus, K15) do not have this problem, since they
have no sibling interface to report separately.
