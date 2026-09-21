# Existing-behavior repair pass — 2026-09-10

This pass addresses the ten findings from the local code review. It does not add
controller models or resume the signed-driver/certification work.

## Repairs

1. Assignment editors capture the controller session, confirmation generation,
   layer, and control. Physical selection changes cannot redirect a save. A stale
   target is rejected while the editor retains the draft.
2. Add-step buttons append. Only **Apply changes** replaces the step being edited.
   Existing steps and disconnected MIDI destinations remain intact during editing.
3. Learning controls and changing assignments merge into generic layouts instead
   of regenerating them. Positions, sizes, ordering, colors, workspace settings,
   and analog calibration survive.
4. Macro cancellation and output ownership changes share a dispatch boundary.
   Failed releases remain tracked for retry; completion cleanup failures are
   reported instead of silently discarding the execution.
5. Direct keyboard mappings and action sequences share keyboard ownership, so
   releasing one mapping does not release another mapping's held modifier.
6. A RunOnce release sequence waits for its matching press sequence. MIDI notes
   are tracked across that boundary and cleaned up on cancellation or failure.
7. Synthetic keyboard/G13 cleanup releases are marked as cleanup, not intentional
   user releases, so unplug/refresh/session changes cannot start release macros.
8. MIDI input monitors device inventory without requiring Refresh. Topology loss
   disarms the controller and cleans up output. Native callbacks use an ordered,
   bounded managed queue with session-generation checks; stale input is rejected.
9. Shutdown saves do not wait for a continuation on the blocked UI dispatcher.
   A failed save does not mark the session clean.
10. Invalid profile collection entries and wrongly typed identifiers are rejected
    as invalid data so the existing quarantine/last-known-good recovery works.

## Verification scope

Regression tests use simulated devices and recording/failing output sinks. They
exercise cancellation races, shared keys, delayed MIDI pairing, stale input,
automatic device loss, editor preservation, layout persistence, blocked-UI
shutdown, and malformed-profile recovery without sending real controller output.

Final local verification: **468 tests passed** (Core 65, Windows 173, App 127,
InputBroker 16, G13Hil 25, OutputWitness 62), up from 405 at the review baseline.
The full Release build completed with zero warnings/errors; formatting verification
and `git diff --check` passed. The final portable package passed both published-app
and fresh-ZIP startup audits, each reporting zero injected inputs.

The final package and SHA-256 manifest are under
`artifacts/packages-20260910-review-fixes-final/`. Earlier same-day packages are
intermediate checkpoints, not the final repair build.

Physical G13/MIDI unplug checks, native-driver compatibility, signing, and
anti-cheat certification are separate hardware/release gates. This pass does not
claim those gates passed and does not install or load a driver. GitHub was not
updated as part of this repair pass.

Existing output limitations outside the reviewed fixes remain: WinMM output uses
one open destination at a time, and recovery for a partially accepted native
mouse-click or Unicode batch was not changed in this pass.
