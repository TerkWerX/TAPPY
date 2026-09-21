# Controller combinations — 2026-09-19

## Create and assign

Check 2–8 physical button tiles on one confirmed source controller, then choose **Create combination…**. A separate **Combo:** tile appears beside the physical layout. Select it and use the ordinary assignment editor for a run-once macro, or **Bank control…** to change a bank on any simultaneously confirmed destination controller. The original member assignments remain saved.

The selection checkboxes still support moving and coloring groups; checking them alone does not create a combination. Select a combination tile and choose **Remove combination…** to remove its definition and assignments in every bank, keeping all physical buttons and their original assignments.

## Microsoft numpad → original Tartarus example

Keep the existing `/`, `*`, and `−` bank controls for banks 1, 2, and 3. Create these additional combinations and configure each new tile's Bank control with the Tartarus as destination:

| Simultaneous source buttons | Operation | Destination |
| --- | --- | --- |
| `/` + `*` | Go to bank | 4 · Yellow + green |
| `/` + `−` | Go to bank | 5 · Yellow + blue |
| `*` + `−` | Go to bank | 6 · Green + blue |
| `/` + `*` + `−` | Toggle two banks | First: 7 · All three; second: 8 · All off |

With the target's **Follow bank changes** enabled, its verified indicator LEDs follow these changes. The toggle selects bank 7 from any other bank, and bank 8 when already in bank 7. This changes real assignment banks, not just the lights.

## Deliberate release semantics

Press the combination together, then release **every participating button**. Only the largest uniquely matched combination runs. Its single-button and smaller combination assignments do not run first. No timers or speculative output are used. Participating single buttons also wait for release. Buttons outside configured combinations keep their existing behavior.

A definition without an effective assignment in the current bank does not defer its members. Ordinary combo macros are per-bank; bank controls remain overlays in every source bank. Rehearsal suppresses macro output but permits logical bank navigation. Physical LEDs retain the existing explicit follow/sync rules.

Combinations and participating member assignments must be run-once; held/repeating mappings are refused rather than silently shortened. Conflicting same-size matches and repeated strokes before the group is fully released are cancelled. Disconnect, emergency stop, profile replacement, lifecycle cleanup, and Rehearsal transitions discard pending gestures. Unavailable or changed destinations are never substituted or queued.

## Scope and hardware limits

The shared engine supports keyboard/raw input, G13 HID, and MIDI button/note paths. Members belong to **one source controller**; the destination can be another controller. Combining a button on one device with a button on a second source device is not implemented. Maximum: 128 combinations per controller, 2–8 members each. Hardware must report the buttons simultaneously; firmware rollover or indistinguishable reports cannot be repaired by software. Pass-through native input is unchanged.

## Persistence and validation

Profile schema 5 stores stable combination identities, members, separate layout tiles, and bank toggle destinations. Existing macros, per-bank assignments, and geometry remain intact. Older builds that support schema 4 cannot load profiles saved by this build; keep the pre-upgrade profile backup when rolling back.

Automated tests cover all 252 subset/press-order/release-order cases for three overlapping buttons, ambiguous gestures, repeats, cleanup, Rehearsal, macro suppression, cross-controller LED routing and toggles, G13/MIDI paths, snapshot roundtrips, stale sources, held-member rejection, movable saved tiles, and themed bank-editor rendering. Automated input and LED backends are simulated: the user's physical three-button rollover and actual LEDs still need hands-on confirmation. This is application verification, not driver certification.

### TESTBED handoff

- Full solution: 660 tests passed (Core 84, Windows 249, App 224, OutputWitness 62, G13Hil 25, InputBroker 16).
- Release: `C:\TappyLab\20260919-combinations\app\Tappy.exe`.
- SHA256: `924BC29A3DE4B88929D3348BCD727BC763E2C5A722963FF0E293AFCC5659245D` (matched local/remote).
- Existing **Tappy Tartarus LEDs Test** desktop shortcut updated; app not automatically launched.
- Seven profile/data files backed up under `C:\TappyLab\20260919-combinations\profiles-before-combinations`; previous shortcut also retained in the release folder. Prior executable retained. No GitHub update or driver/security-policy changes.
