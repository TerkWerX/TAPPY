# LED animations

> Status: Milestones 1 (single-device procedural effects), 2 (hand-painted
> frame-by-frame animations), and 3 (multiple physical grids sharing one
> animation across a shared canvas) are all implemented, covered by
> deterministic tests, and physically confirmed on a real Akai Fire. See
> [D-032](DECISIONS.md#d-032--any-number-of-confirmed-led-grid-controllers-can-share-one-animations-canvas)
> for the Milestone 3 confirmation and
> [D-034](DECISIONS.md#d-034--the-frame-by-frame-editor-milestone-2-is-implemented-and-physically-confirmed)
> for Milestone 2's.

## What's new vs. what's reused

No new hardware-protocol facts are introduced by this feature. Animation
output reuses the Akai Fire's already-verified pad SysEx transport
(`FirePadColorSysEx`/`FireRgb`, see [`AKAI_FIRE.md`](AKAI_FIRE.md)) and the
APC MINI's already-verified Note-On velocity lighting
(`ApcMiniVelocity`/`TryParseApcMiniLedNote`) verbatim — an animation is simply
that same per-pad color-setting call, made repeatedly on a timer instead of
once. Device output routing reuses the exact-port-name matching from D-024 in
[`DECISIONS.md`](DECISIONS.md) unchanged.

## Scope

Animations are available only for grid-style, per-control-addressable
lighting: the Akai Fire (full RGB) and the APC MINI (bicolor: off/green/red/
amber). This is enforced structurally, not by a special-cased exclusion list —
`LedGridAdapterCatalog.Find` only has entries for models with a genuine pad
grid, so a confirmed Logitech G13 (one whole-device RGB zone,
`ControllerLightingTopology.Global`) or Razer Tartarus (3 discrete indicator
lights, no addressable grid at all) simply has no adapter, `MainViewModel.
HasAnimatableLighting` is `false`, and the "Animations…" button never appears
for those controllers. Both keep their existing static-color-only lighting
unchanged.

## Architecture

- **Data model** (`Tappy.Core.Models`): `LedAnimationDefinition` (an effect or
  a hand-authored frame list, loop flag, playback speed multiplier, canvas
  size, and a `Participants` list — populated with one entry today, ready for
  Milestone 3's multiple physical grids without a breaking format change) and
  `LedEffectDefinition` (kind, colors, speed, and effect-specific parameters).
  Follows the same flat-fields-with-`Normalize()`/`Clone()` style as
  `ControllerActionSequenceDefinition`.
- **Effect math** (`LedEffectMath`): pure, deterministic
  `(elapsedMs, column, row) → RGB` functions for five starter effects —
  Rainbow Wave, Color Chase, Breathe, Sparkle, Color Wipe. No hardware or WPF
  dependency; fully unit-tested.
- **Grid adapters** (`Tappy.App.Services.ILedGridAdapter`): bridge the
  animation engine to one specific controller model's real pad addressing,
  wrapping the existing `TryParseAkaiFirePadIndex`/`TryParseApcMiniLedNote`
  formulas rather than duplicating protocol logic. This is the extension
  point for future grid controllers — one new adapter class plus one catalog
  entry, nothing else in the animation engine changes.
- **Playback** (`LedAnimationPlayer`, owned by `ControllerWorkspace`, ticked
  by a `DispatcherTimer` in `MainWindow.xaml.cs` at ~15fps, the same ownership
  convention as the existing reconnect-poll and zoom-persist timers): each
  tick evaluates colors, updates the confirmed tab's tile preview
  synchronously (never triggering a profile save — see D-025), and pushes the
  same colors to hardware off the UI thread, since the Fire's `SendSysEx`
  blocks the calling thread until Windows confirms delivery.
- **Library persistence** (`AnimationLibraryStore`): a cross-tab, named
  library of saved animations, stored once under Tappy's shared data
  directory (not inside any one device tab's own profile — an animation isn't
  scoped to a single controller's saved layout). The currently open animation
  autosaves continuously on a short debounce as you edit, the same
  "reversible, cosmetic state just keeps saving" convention already used for
  tile zoom and position — there is no separate "unsaved draft" to lose.
- **Frame-by-frame playback** (`Tappy.Core.Models.LedFramePlayback.GetActiveFrameIndex`):
  a pure, deterministic function in the same style as `LedEffectMath` — walks
  each `LedAnimationFrame`'s `DurationMs` cumulatively to find which frame is
  active at the current elapsed time, wrapping when `Loop` is set or clamping
  to the last frame otherwise. `LedAnimationPlayer.Tick` computes this once per
  tick (the same frame answer applies to every participant's cells, not a
  per-cell computation) and, for Frames-kind animations, looks each cell's
  color directly out of the active frame's `Cells` dictionary
  (`"{column},{row}"` in shared-canvas coordinates) instead of evaluating
  `LedEffectMath` — both paths converge on the same
  `target.Adapter.ResolveColor(...)` call either way, so a frame cell's stored
  named color key or `#RRGGBB` hex downgrades for the APC MINI exactly like an
  effect-computed color already did. `Play()` refuses to start (returning
  `false`, same as the no-resolvable-participant case) for a Frames-kind
  animation with zero frames.
- **Frame editor** (`LedAnimationWindow`'s Frames-mode panel,
  `LedAnimationEditorViewModel`'s frame/canvas members): a frame timeline
  (Add/Duplicate — inserts immediately after the current frame —
  /Delete/Move up/Move down, each frame's duration independently editable) next
  to a paintable canvas sized to the animation's current `CanvasColumns ×
  CanvasRows` (the same size Participants already drives for effects) and a
  paint-color palette reusing the exact `TileColorChoice` table the physical
  controller grid's own color picker already uses, plus the existing
  `FireColorPickerWindow` for arbitrary hex. Painting with the palette's
  "Off / default" swatch removes the cell's dictionary entry rather than
  storing an explicit "Default", keeping saved JSON sparse. A Frames-kind
  animation is never left with zero frames — enforced centrally in
  `RefreshFrameChoices()` so it self-heals whether that state came from the
  live "switch to Frames mode" action or from loading/selecting an animation
  that already happened to have `Kind: Frames` with none.
- **Shared-canvas participants** (`ControllerWorkspace.GetAnimationTargets()`,
  `LedAnimationParticipantLayout.Rearrange`, both in
  `ControllerWorkspace.Animations.cs`): the Animations window's Participants
  section lists every confirmed tab with a registered grid adapter as a
  checkbox. Checking one appends it to `Participants` immediately to the
  right of the last checked one; unchecking re-flows the rest. `LedAnimationPlayer.Tick`
  re-resolves every participant against `GetAnimationTargets()` on every
  tick — the same re-validate-before-every-write discipline D-024 established
  for bank routing — so a participant that disconnects mid-play drops out of
  that tick only, and the rest keep animating. See
  [D-032](DECISIONS.md#d-032--any-number-of-confirmed-led-grid-controllers-can-share-one-animations-canvas).

## Required finite validation

1. ~~Confirm a real Akai Fire, open Animations, run Rainbow Wave... and
   confirm the pads visibly animate.~~ **Confirmed 2026-09-20** — see D-032:
   two real Fires and a real APC MINI played Rainbow Wave together as one
   continuous shared canvas.
2. ~~Confirm `Stop` actually clears the pads rather than freezing the last
   frame lit...~~ **Confirmed 2026-09-21** on a real Fire: after a two-frame
   painted animation was playing (visibly cycling between the painted
   colors), clicking Stop turned every pad off rather than freezing the last
   frame lit — see D-034. Closing Tappy mid-playback specifically remains
   untested by hand.
3. ~~Repeat on a real APC MINI...~~ **Confirmed 2026-09-20** as part of the
   same three-device shared-canvas run in D-032.
4. ~~Confirm the animation library survives an app restart...~~ **Confirmed
   2026-09-21**, though as a window close/reopen rather than a full app
   restart: a painted two-frame animation, its per-frame durations, and its
   Participants selection were all still present, unchanged, after closing
   and reopening the Animations window — see D-034.
5. Uncheck a participant while an animation is playing and confirm the
   remaining participant(s) keep animating without the app hanging, then
   re-check it and confirm it rejoins the shared canvas at its same
   left-right position. **Not yet confirmed by hand** (covered only by
   `A_disconnected_participant_is_dropped_from_the_tick_but_the_other_keeps_animating`'s
   deterministic assertions).
6. Frame-by-frame specific: paint at least two frames with different colors
   and durations on a real Fire, Play, and confirm the pads visibly cycle
   between the painted patterns at roughly the authored timing.
   **Confirmed 2026-09-21** — see D-034.
