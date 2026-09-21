# Tappy decision record

These decisions are normative for the first milestone. A later change that affects
security, source behavior, hardware, branding, licensing, or public state requires
product-owner review.

## D-001 — Device-aware pass-through is the only initial source mode

**Status:** Accepted.

**Decision:** Store requested source mode separately from effective source mode.
Version 0.1.0 permits only `PassThrough`; unavailable `GlobalBlock` or `Exclusive`
requests resolve to `NeedsAttention`, with no mapping output until reviewed.
**Reason:** Raw Input identifies a physical device but does not selectively suppress
its ordinary system input. `RIDEV_NOLEGACY` changes messages delivered to the
registering application, not system-wide per-device delivery. Timestamp correlation
between a low-level hook and Raw Input is rejected as race-prone.

## D-002 — Selection is an explicit state machine

**Status:** Accepted.

**Decision:** Enumeration alone never arms a device. The user must choose a listed
device, start an identification capture while all tracked controls are released,
press and release that same device, and click a separate confirmation action.
Unselected signals are discarded without state/history logging.
**Reason:** A convenient first-device fallback could capture or map the primary
keyboard.

## D-003 — Physical control identity is scan/usage based

**Status:** Accepted.

**Decision:** Raw keyboard `ControlId` includes provider, usage-page/collection, scan
code, and E0/E1 state. Virtual key and display label are metadata only. Other
providers supply their native HID/MIDI/joystick identity.
**Reason:** Characters and virtual keys collapse numpad/navigation, modifier, OEM,
locale, and extended-key distinctions.

## D-004 — Key-down freezes release behavior

**Status:** Accepted.

**Decision:** Key-down creates an execution lease containing controller, control,
profile revision, layer, binding, application scene, effective source handling, and
release action. Key-up consumes that lease even if the active profile/context has
changed. Disconnect and lifecycle cleanup consume all leases owned by the affected
scope.

## D-005 — Injection protection is layered

**Status:** Accepted.

**Decision:** Tag Tappy `SendInput` with a per-process nonzero marker; reject matching
Raw Input `ExtraInformation`, reject device-less injected records, track execution
ancestry, cap nesting, and enforce a sliding output-rate limit. On violation, cancel
the branch and release its owned output. Timestamp matching is not used.

## D-006 — Fail open, release owned output

**Status:** Accepted.

**Decision:** Registration, backend, queue, or source-mode failures show
`Needs attention`, disarm mapping output, and release all output Tappy owns. The
physical keyboard remains ordinary pass-through input.

## D-007 — Profiles are immutable snapshots with isolated identity

**Status:** Accepted.

**Decision:** The runtime swaps complete normalized snapshots. Storage uses
`%LOCALAPPDATA%\Tappy`, `default.tappy.json`, atomic replacement, last-known-good,
and corrupt quarantine. It never opens `%LOCALAPPDATA%\Tippy`. Portable mode uses a
Tappy-only adjacent data directory and marker.

## D-008 — Three layers are a default, not a schema limit

**Status:** Accepted.

**Decision:** New controller profiles begin with three layers for sister-product
parity. Collections are variable-length and validation permits more.

## D-009 — Generic layouts precede product artwork

**Status:** Accepted.

**Decision:** The initial app renders reviewed data-driven generic grids. Files in
`PAD IMAGES` are protected references/derivatives and are excluded from packages
until exact model, source/license, processing history, and approval are recorded.

**2026-09-03 amendment:** Assignment controls remain clean, selectable grid tiles;
device artwork is a separate, non-interactive locator beside the grid. A selected
tile and its physical input share one state object, so either path illuminates the
matching photo hotspot and simultaneous held inputs remain visible. The first
approved exception is the owner's own G13 photograph, embedded as a transparent
resource and gated by exact `raw-hid-g13`/`046D:C21C` identity. No other reference
image is approved by this amendment.

## D-010 — Unique application/release identity

**Status:** Accepted.

**Decision:** Use executable `Tappy.exe`, root namespace `Tappy.*`, data root
`%LOCALAPPDATA%\Tappy`, AppUserModelID `TerkWerX.Tappy`, mutex
`Local\TerkWerX.Tappy.HandController.0_1`, startup value `Tappy`, emergency hotkey
`Ctrl+Alt+Shift+F12`, a newly generated installer AppId, Tappy extensions, and only
Tappy endpoints. The reserved update endpoint is the Tappy-only GitHub
`releases/latest` API URL; no update check is implemented or enabled in this
milestone. The application, window, and tray icon is generated from the
owner-approved single-letter tattooed-hand artwork.

## D-011 — Source publication is authorized; release decisions remain open

**Status:** Partially resolved 2026-09-02.

The owner authorized publishing Tappy source, documentation, and CI configuration to
the public `https://github.com/TerkWerX/TAPPY` repository. Public software license,
verified device list, signing
certificate acquisition, packaged software release, website
publication, and production hosting remain open or deferred and are not implied by
source-publication authorization. Until license and contribution terms are selected,
all rights are reserved and external contributions must not be submitted or merged.

**2026-09-04 driver amendment:** The owner authorized full engineering,
certification, signing-preparation, and anti-cheat review for an optional Tappy
exclusive-input driver. This does not assert that a certificate or Partner Center
account exists, does not authorize silent installation of development code, and does
not by itself approve a public binary release. D-017 controls that subsystem.

**2026-09-03 branding amendment:** The owner approved the supplied `TAPPY_hand_T`,
`TAPPY_hand`, and `TAPPY_logo` images for Tappy application use, requested the same
placement scheme used by Tippy, and selected the single-letter hand for the app icon.
This resolves the mascot/wordmark/application-icon decision only; it does not
authorize signing, a packaged binary release, website publication, or a public
software license.

## D-012 — ContainerId is authoritative for physical keyboard grouping

**Status:** Accepted.

**Decision:** Multiple Raw Input keyboard interfaces are one logical controller only
when Windows supplies the same nonempty ContainerId. VID/PID, display name, device
path similarity, USB topology, or timing alone never merges interfaces. Interfaces
without that evidence remain separate choices.

**Reason:** The attached Freewolf K15 candidate exposes four keyboard interfaces at
`1A2C:2D43` within one authoritative ContainerId group. Grouping those interfaces
prevents duplicate controller choices without conflating separate identical
devices.

## D-013 — Logitech G13 support is exact and model-specific

**Status:** Accepted; attended input and lighting spot checks complete, formal HIL pending.

**Decision:** The dedicated G13 provider accepts only the physical `046D:C21C`,
`RIM_TYPEHID`, `FF00:0000` collection, validates its fixed eight-byte input report,
and exposes 39 code-defined controls. `046D:C232` is the G HUB virtual keyboard and
is never G13 identity. The one G13 RGB backlight zone is written only through the
confirmed physical controller's exact persistent `046D:C21C` interface, using its
model-specific five-byte `0x07` feature report. Report `0x05` controls the separate
M1/M2/M3/MR indicator LEDs and must never be mistaken for RGB. Tappy does not claim per-key G13
RGB, generic Logitech, learned-HID, LCD, or memory-mode support.

**Reason:** Exact matching and strict decoding keep a model-specific protocol from
becoming a misleading generic-HID claim. Targeting the confirmed persistent identity
also avoids a broad Logitech SDK call that could recolor another Logitech device.
Deterministic tests support the code. On 2026-09-04 the owner confirmed that a direct
`0x07` purple report changed the attached G13 while the G910 remained under its normal
lighting control. That narrowly verifies the target and RGB report; the G13 remains
below Functional/Verified until the complete finite HIL run succeeds.

## D-014 — External implementation provenance stays clean-room

**Status:** Accepted.

**Decision:** Public platform and protocol documentation may inform an independent
Tappy implementation. An owner-authorized internal donor audit found no reusable
G13 or SpacePilot implementation; no donor code, repository history, or logs were
copied. Tappy's G13 support was implemented clean-room from public platform/protocol
documentation and project-owned design.

**Reason:** This preserves an auditable public-source boundary without implying
protocol compatibility between the G13 and SpacePilot Pro.

## D-015 — MIDI input is a first-class controller provider

**Status:** Accepted; physical APC MINI spot check observed, finite all-control run pending.

**Decision:** A selected WinMM MIDI input port enters the same identify-confirm-map
pipeline as a Raw Input keyboard or G13. Notes have held press/release semantics,
note-on velocity zero is release, and program changes plus CC increase/decrease are
balanced pulses. Once normalized, a MIDI control may use every ordinary Tappy output
step, including keyboard, text, mouse, program, PowerShell, MIDI, and OSC.

WinMM identity is deliberately marked Ambiguous because its legacy capabilities do
not expose a stable serial or physical-port identifier. Velocity gates, SysEx, and
clock remain deferred. Exact-model analog visuals use the original CC value in
addition to the balanced directional mapping pulse.

**Reason:** Input transport and output action are orthogonal. Reusing the mapping
engine gives a MIDI pad full Tappy functionality while balanced directional pulses
keep faders and knobs from becoming stuck key state. The implementation is native
and project-owned; installed commercial applications were treated only as behavior
references and no proprietary code was copied.

## D-016 — Continuous photo controls are typed and calibrated

**Status:** Accepted; APC MINI v1 fader implementation complete, additional exact-model definitions pending.

**Decision:** Controller-photo hotspots distinguish buttons, faders, bounded
potentiometers, and endless rotary encoders. A fader replaces the photographed
track region with a neutral channel and moves only its cap. A bounded pot uses a
position line over a reviewed physical sweep. An endless encoder accumulates a
wrapping line and exposes saved direction and sensitivity controls. Directional
mapping IDs that describe one physical analog control share a single visual.

Beginning, optional center, and end raw values are stored in profile schema 3 for
bounded controls; descending ranges intentionally represent reversed hardware.
Encoder degrees-per-event and direction are stored beside them. No photo-series
candidate receives analog behavior until its exact MIDI/HID semantics and geometry
are reviewed.

**Reason:** Live position feedback makes the photo an accurate instrument status
display without confusing the assignment grid or falsely implying a controller
capability. Per-device calibration handles truncated, reversed, and off-center
hardware ranges and remains portable with community profiles.

## D-017 — Exclusive keyboard input is an optional signed subsystem

**Status:** Architecture and activation policy accepted 2026-09-04; unsigned KMDF
lab scaffold, managed wire client, and status-only broker/IPC scaffold built, but
never signed, installed, loaded, or enabled.

**Decision:** Tappy may add a separate KMDF keyboard filter and least-privilege broker
so a physically verified secondary keyboard can be copied into Tappy and suppressed
before it reaches ordinary Windows applications. The user must first choose and
physically verify a different primary keyboard that is always passed through.

Exclusive activation requires all of the following at the same time: exact stable
identity for both devices, explicit administrator installation and consent, an
authenticated broker, driver acknowledgement of both roles, a 250–2,000 ms kernel
fail-open watchdog with a current heartbeat, a working mouse/tray path, an emergency
stop on the protected primary keyboard, HVCI compatibility, and an acceptable signed
package. Production Tappy requires a Microsoft-signed HLK-certified package. It will
not arm exclusive input during test-signing, kernel debugging, or a detected protected
application. Any missing or stale condition atomically returns every keyboard to
pass-through.

The driver will not inject input, inspect processes, patch code, conceal itself,
communicate over a network, or offer a general-purpose privileged IOCTL. The WPF app
remains non-administrative; only the installed broker may open the driver control
interface. Driver installation, enablement, and removal remain explicit operations
with a documented recovery path. No compatibility claim is made for an anti-cheat
product or game until its exact current version has passed the release matrix and,
where necessary, vendor review.

**Reason:** Raw Input can attribute but cannot suppress one keyboard. A kernel filter
can perform true per-device suppression, but a mistake can remove the user's only
typing path or conflict with security software. The stronger release gate is a
deliberate product policy, not a claim that every Windows client installation
technically requires HLK certification.

## D-018 — Remembered-reconnect eligibility survives an app restart, for non-ambiguous devices only

**Status:** Implemented 2026-09-19; not yet physically verified on TESTBED.

**Decision:** A device-tab slot's saved profile now records the persistent id of
whichever controller it last confirmed. If, on the next Tappy launch, that exact
device is already connected and its identity is unambiguous, the slot silently
rearms it — same neutral-then-confirm handshake, same Rehearsal-Mode-only landing,
same "no identification gesture is needed" messaging already used for a live
mid-session USB reconnect. Ambiguous identities are excluded exactly as they
already are mid-session: WinMM MIDI controllers (Akai Fire, APC MINI) always
require manual identification, on every launch, because WinMM exposes no verified
hardware serial to trust. A protected typing keyboard is never restored this way.

Window layout position/size/mode already only saved at clean exit; the workspace
zoom level (`ControllerWorkspaceScale`) previously was not saved at all and always
reset to 1.0 on launch. It is now saved too — see D-019 for where, which revises
this decision's first (window-level) placement of it after it turned out wrong.

**Reason:** The owner explicitly asked for "however it looks when it closes" to
match "how it looks when it opens," including controllers that never left the PC
— repeating full physical identification for a device that was never actually
disconnected was pure friction, not a safety check. The prior "session-only, not
startup" boundary in `REMEMBERED_RECONNECT.md` was a deliberate choice, not an
oversight, so this was resolved as an explicit product decision (owner chose the
"keyboard/G13 auto-reconnect, MIDI stays manual" option over also trusting MIDI's
ambiguous identity, and over requiring one extra confirmation click for every
class) rather than silently reinterpreted. Keyboard-class devices and the G13 keep
a Windows-verified stable identity (ContainerId-derived) that does not depend on
Tappy's own process having stayed alive to observe it, so trusting it again after
a restart carries the same risk profile as trusting it again after a mid-session
unplug/replug — already an accepted case. MIDI's identity is a bare port name plus
enumeration order, which cannot make the same claim, so weakening its exclusion
was rejected rather than quietly extended for consistency's sake. Every restore
still lands in Rehearsal Mode, never live output, matching the existing safety
floor for every other reconnect path.

## D-019 — Workspace zoom and photo-pane width are per-controller, not per-window

**Status:** Implemented 2026-09-19, superseding D-018's first placement of zoom
persistence; not yet physically verified on TESTBED.

**Decision:** `ControllerWorkspaceScale`'s zoom factor and the photo-pane splitter
width are stored on `ControllerLayoutDefinition` (Core), alongside the grid
columns/rows/snap settings that were already there — one value per confirmed
controller's own saved layout, round-tripped through `ControllerLayoutWorkspace`
exactly like tile positions already are (`GetControllerLayout`/
`UpdateControllerLayout`/`SaveControllerLayoutAsync`). `WindowPlacementStore` no
longer carries either value; it is back to pure window chrome (position, size,
mode, monitor). `MainWindow` re-applies the currently displayed tab's own zoom and
pane width to its one shared `ControllerWorkspaceScale`/`ControllerPhotoColumn`
every time the visible tab changes (`ShowTabViewModel`), not just once at startup.

**Reason:** D-018 first saved zoom in `window-state.json`, since at the time it
looked like ordinary window chrome. The owner found this was wrong by using it: with
multiple device tabs open, zooming one tab's grid visibly distorted every other
tab's grid the moment its tab was selected, because one `ScaleTransform` was being
shared window-wide across tabs whose grids have completely different natural
proportions. The photo-pane width had the same latent bug, inherited from before
this session — it was already window-level, just not yet noticed. Each device tab
already has its own independent `MainViewModel`, runtime, and saved profile; zoom
and pane width needed to join the layout data that was already scoped that way,
not the window chrome that genuinely is shared.

**Second bug found in the same fix, same day:** moving zoom here alone was not
enough — the owner reported it still did not survive switching tabs and back, even
after this change landed. `Runtime_OnStateChanged` calls `SynchronizeControllerLayout`
on essentially every runtime event (any control press, a bank change, anything that
calls `RaiseState`), which re-reads `GetControllerLayout()` and overwrites this
viewmodel's `Zoom`/`PhotoPaneWidth`. Zoom's disk save was debounced 600 ms (to avoid
a file write per scroll-wheel tick), but nothing pushed the new value into the
runtime's own in-memory layout until that debounce fired — so almost any incidental
event in that 600 ms window (routine, not something the user was even doing on
purpose) silently reverted the in-memory viewmodel value before the save ever ran.
Photo-pane width was not affected: its save was already immediate (on
`DragCompleted`), so there was no window for anything to interleave. Fixed by
splitting `MainViewModel.SaveControllerLayoutAsync` into an
`ApplyControllerLayoutToRuntime` half (pushes the current viewmodel state into the
runtime's in-memory layout, synchronous, no disk I/O) and the disk-persist half;
`MainWindow.HandleControllerLayoutZoom` now calls the in-memory half synchronously
on every zoom tick (via the new public `SyncLayoutPresentationToRuntime`), and only
debounces the actual `SaveProfileAsync` disk write. Covered by
`Zoom_survives_an_unrelated_control_event_before_the_debounced_save_runs`
(`MultiDeviceWorkspaceTests.cs`), which reproduces the exact sequence (zoom, then an
unrelated `RaiseState`-firing event, before any save) and was confirmed to fail
without this fix before being left in place passing.

## D-020 — An unconfirmed startup-reconnect candidate survives a benign membership change

**Status:** Implemented 2026-09-19; not yet physically verified on TESTBED.

**Decision:** In `DeviceAwareControllerRuntime.ProcessDeviceChanged`, when the
currently selected device reports `RawInputDeviceChangeKind.MembershipChanged`
(one of its own interfaces arriving or leaving separately, while the device as a
whole was never unplugged), `_rememberedReconnect` is now preserved — instead of
wiped outright — specifically when the disrupted selection is an unconfirmed
candidate that D-018's startup reconnect silently armed and a human has not yet
touched (`_confirmed is null && _isRememberedReconnect && _candidate is
{ IsAmbiguous: false }`). The status message for this case also now matches an
ordinary, self-healing disconnect ("the same recognized device will return
automatically") rather than "select and identify it again." Anything already
`_confirmed` — an actively used, human-verified session — keeps the prior,
stricter behavior untouched: a membership change there still wipes
`_rememberedReconnect` and requires a fresh manual identification.

**Reason:** The owner asked why the Razer Tartarus keypad — a genuine,
non-ambiguous keyboard-class device, not MIDI — never survived D-018's silent
startup reconnect, while the Microsoft Bluetooth Number Pad always did. Diagnosis
(profile inspection proving the saved identity was byte-identical across restarts,
then temporary instrumentation of the actual match/arm/confirm path, described in
full in this session's own investigation) found that Tartarus is a multi-interface
composite device (several keyboard-usage-page collections across two USB
interfaces); Windows reports each interface's arrival separately even though the
whole unit stayed connected the entire time. `RawInputKeyboardProvider` already
classified this as `MembershipChanged` rather than a fresh arrival (existing,
pre-session logic, covered by its own tests), and `ProcessDeviceChanged` already
treated any membership change on the selected device as more suspicious than an
ordinary disconnect, wiping trust outright and demanding re-identification — a
deliberate choice for a device someone is *actively using*. D-018's startup
reconnect arms a candidate within roughly a second of launch, before Windows
finishes delivering that interface's own arrival burst, and that policy — sound
for a confirmed, in-use session — was inadvertently swallowing the reconnect
attempt every single time for any multi-interface device, before it ever reached
confirmation. The owner explicitly approved narrowing this specifically for
unconfirmed startup candidates (option chosen: "Yes, narrow fix") rather than
leaving multi-interface devices always requiring one manual identification per
restart, since the underlying identity-verification safety net — exact
PersistentId/vendor/product/usage match on every retry — remains fully intact
regardless; a genuinely different device would still fail that match and never
reach confirmation. Single-interface devices (Number Pad, Targus, K15) never
trigger this at all, since they have no sibling interface to report separately.
Covered by
`Startup_reconnect_survives_a_benign_membership_change_on_a_multi_interface_device`
(`RememberedReconnectTests.cs`), which constructs a synthetic two-interface
composite descriptor, arms it via startup reconnect, fires the same
`MembershipChanged` notification a real multi-interface device produces, and
confirms reconnection completes instead of falling back to manual identification;
confirmed to fail without this fix before being left in place passing.

## D-021 — The Akai Fire's full RGB picker extends the same ColorKey/SysEx path, gated by a capability flag

**Status:** Implemented 2026-09-20; only the pre-existing starter-palette colors
have been physically verified, not yet arbitrary custom RGB (see AKAI_FIRE.md).

**Decision:** `ControllerLedColorCapability` gained a `SupportsFreeformColor`
flag (default `false`); only the Fire's `GetControllerLedColorCapability()`
branch sets it `true`. A custom color is represented as an ordinary `"#RRGGBB"`
string stored in the same `ControllerControlPlacement.ColorKey` field the named
starter palette already used — no new profile schema field, no new persistence
path. `DeviceAwareControllerRuntime.FireRgb` now tries
`TryParseHexColor` first and only falls through to the existing named-color
switch when the string is not that shape, so the starter palette and the picker
share one SysEx encoder unchanged. `MainViewModel.ApplyTileColor`/
`ResolveSupportedColorKey` special-case a hex `ColorKey` only when
`HasFreeformColorSupport` is true, so no other controller can accidentally end
up with a hex `ColorKey` it has no way to render. The picker itself
(`FireColorPickerWindow`) is a small dark-themed dialog — three 0–255 sliders
plus a synced hex textbox and live swatch preview — deliberately without an
eyedropper, color history, or HSV wheel; the hardware need is exhaustively met
by "any 24-bit RGB value," and those extras would be scope creep with no
requested use case.

**Reason:** The owner asked to resume the Fire roadmap ("its RGB and other
lights and it's screen") now that the starter-palette SysEx transport was
physically verified. A free-form picker was the next piece: the transport and
wire format were already proven, so the only new work was letting an arbitrary
value reach that same encoder, plus a UI to choose one. Reusing `ColorKey`
avoided a schema/persistence change entirely, since it was already a free-form
string with no palette validation at the model layer.

While building this, the same "stomping" bug class fixed for per-tab zoom in
D-019 reappeared: `MainViewModel.SynchronizeTileColorChoices` runs on every
`Runtime_OnStateChanged` firing (confirmed, mode changes, disconnects — not
ordinary control presses) and unconditionally cleared `TileColorChoices` and
reset `SelectedTileColor` back to index 0. A custom color the user had just
picked but not yet applied/synced would be silently discarded by any unrelated
state event in that window. Fixed the same way as D-019: the currently
selected custom color is detected before the rebuild and, if the confirmed
controller's model identity has not changed, re-added and re-selected after the
named list is rebuilt.

Covered by `Freeform_color_capability_offers_a_custom_color_that_applies_and_syncs`,
`A_picked_but_unapplied_custom_color_survives_an_unrelated_runtime_state_event`,
and `Non_freeform_controller_never_offers_a_custom_color_button`
(`MainViewModelTests.cs`), plus `Hex_color_parsing_accepts_only_a_well_formed_rrggbb_value`,
the extended `Akai_Fire_visual_colors_map_to_starter_palette_rgb` hex cases, and
`Akai_Fire_led_capability_advertises_freeform_rgb_support`
(`DeviceAwareControllerRuntimeTests.cs`). The stomping-regression test was
confirmed to fail without its fix before being left in place passing.

## D-022 — A saved code-defined layout whose Id no longer matches the current shape is reset, not scrambled into it

**Status:** Fixed and physically confirmed 2026-09-20. The owner's real,
already-broken TESTBED profile was diagnosed before shipping the fix, then
the same profile was retested on the physical Fire after deploying it: the
encoders rendered back on their own row, separate from Mute/Solo and the pad
grid, with the previously-set pad colors still in place.

**Decision:** `DeviceAwareControllerRuntime.MergeCodeDefinedLayout` now compares
the saved layout's `Id` against the fresh code-defined (canonical) layout's
`Id`. When they match, it behaves exactly as before: saved position, size, and
per-control ordering are preserved and reapplied onto the canonical row/column
structure. When they don't match — the saved layout predates a change to that
model's shape, or was never one of these code-defined shapes at all (a
generic learned `"generated-grid"` layout from before this exact device name
was recognized) — position, size, and ordering are no longer carried over at
all; every control gets the fresh canonical layout's own position outright.
Only customization that isn't tied to a specific slot (`ColorKey`,
`AnalogRawAtMinimum/Maximum/Center`, `EncoderDegreesPerStep`,
`EncoderReversed`) still carries over, matched by exact control identity, not
by list position. `MidiControllerModelCatalog.AkaiFireV1LayoutId` was bumped
from `-v1` to `-v2` alongside this fix, since it had never been bumped when
the encoders moved to their own row (the change that exposed this bug),
which is what forces every existing saved Fire profile through the reset path
on its next confirm. G13's and the APC MINI's own layout Ids are unchanged, so
this fix is inert for them today — it only changes behavior when a saved
layout's Id actually differs from the current canonical one.

**Reason:** The owner reported the Fire's rotary encoders "jumbled in with the
grid of rgb buttons again" after using the RGB-picker build from D-021.
Comparing the owner's actual saved TESTBED profile (fetched read-only via
SCP) against the current code-defined default showed the encoders correctly
isolated on their own row in a profile that had been freshly confirmed after
the earlier encoder/pad-separation fix, but badly scrambled in an older,
continuously-used profile — literally showing pad-note control IDs inside a
row labeled `"encoder-1"`, and `Modes / Bank` inside a row labeled
`"pad-row-1"`. The root cause: `MergeCodeDefinedLayout` always started from
the fresh canonical row/column *structure* but refilled each of its slots,
one-by-one, with whichever control occupied the same numeric slot position in
the OLD saved order (`ApplyPresentationOrder`) — a strategy that only produces
a correct *reordering* when the canonical structure's shape hasn't changed
since the profile was saved. Since the Fire's row/column shape changed
(encoders moved to their own row, a gap column was added) but its layout Id
never did, there was no way to detect that a given saved profile predated that
change, so the slot-refill scrambling ran on every confirm, forever, for any
profile saved before the shape changed — the RGB-picker build didn't cause
this, it only mattered because the owner happened to be looking at exactly
this pre-existing, previously-unnoticed corruption while trying the new
picker. Assignments are unaffected: they're keyed by control identity in a
separate part of the profile, not by `Layout.Rows` position.

Covered by
`A_stale_saved_Fire_layout_is_reset_to_the_current_shape_not_scrambled_into_it`
(`DeviceAwareControllerRuntimeTests.cs`), which seeds a profile with the old
`-v1` layout Id and a control placed the way the real corrupted profile had
it (an encoder's touch control overlapping Mute/Solo 1 and the first pad
row), confirms the same Fire again, and asserts every encoder control lands
back on row 0 with fresh canonical positions while a `ColorKey` customization
on the same control survives the reset. Confirmed to fail (reproducing the
exact stale position) without this fix before being left in place passing.

## D-023 — A second simultaneously-connected Fire is recognized by its "N - " name prefix

**Status:** Fixed and physically confirmed 2026-09-20 against two real Fire
units connected simultaneously on TESTBED, each getting its own confirmed
tab with the full 99-control layout and photo.

**Decision:** `MidiControllerModelCatalog.Find` now also strips a leading
`"<digits> - "` prefix (a new `DuplicateDevicePrefix` regex) from the WinMM
port name before comparing it against known model names, in addition to the
existing trailing `" (device N)"` suffix strip.

**Reason:** AKAI_FIRE.md's "Multiple Fires" section already recorded, from a
cross-check project's own testing, that a second Fire enumerates with a
leading-number prefix rather than Tappy's own suffix convention — flagged
there as unconfirmed against Tappy specifically. The owner then connected two
real Fire units to TESTBED: Tappy correctly saw a second, distinct MIDI
device, but confirming it produced no photo and none of the Fire's 99-control
layout — it fell through to the generic learn-as-you-press path. Reading the
owner's actual saved profile confirmed the exact live name Windows assigned:
`"MIDI: 2 - FL STUDIO FIRE"`, exactly the prefix pattern the cross-check
project had predicted, now independently confirmed against Tappy's own real
enumeration rather than left as an inference. A device already confirmed
under the generic fallback before this fix (a `"generated-grid"` layout) is
also covered: the next confirm now matches the Fire model correctly, and
D-022's shape-mismatch reset applies (its saved `Id` is `"generated-grid"`,
which never equals the canonical Fire Id), so it gets the proper photo and
99-control layout instead of merging the generic fallback's arbitrary
discovery-order positions into it.

Covered by `A_second_simultaneously_connected_Fire_is_recognized_by_its_prefixed_name`
(`DeviceAwareControllerRuntimeTests.cs`), covering both the plain and
`"MIDI: "`-prefixed forms and more than one duplicate index. Confirmed to
fail (returning no match) without this fix before being left in place
passing.

## D-024 — Fire/APC MINI lighting output is matched by exact port name, not by model

**Status:** Fixed and physically confirmed 2026-09-20: with two real Fire
units connected simultaneously, syncing a color on either tab's "Sync
supported LEDs" changed only that tab's own physical unit — a different
custom color on each unit, both taking effect independently.

**Decision:** `SyncAkaiFirePadColors` and the APC MINI branch of
`SyncControllerLedColors` now select the WinMM MIDI *output* port to send to
by comparing its exact name against the *confirmed device's own* name (a new
shared `MatchMidiOutputPorts` helper, built on a new `StripMidiDisplayPrefix`
helper that strips the `"MIDI: "` prefix Tappy's own input descriptors add,
recovering the raw WinMM port name both the input and output side share).
Previously both matched by asking "is this any output whose name resolves to
the Fire/APC MINI model," with no reference to which specific confirmed
device was asking.

**Reason:** Immediately after D-023 made a second Fire's *input* correctly
recognized, the owner reported that Sync supported LEDs stopped changing
either physical unit's pads at all, while Tappy's own tile/photo preview
still updated normally (that preview is local UI state, not hardware output —
unaffected). The screenshot showed Tappy's own diagnosis: "More than one Fire
output port is present, so Tappy refused to guess which unit to light." That
refusal is itself a correct, deliberate safety behavior (never send output to
a guessed device) — but it now fired for *every* sync, on *either* Fire tab,
because both Fire units' output ports legitimately match the model-based
filter once both are recognized, and the code had no way to prefer the one
that corresponded to whichever tab's "Sync" button was actually clicked.
Matching by the confirmed device's own exact port name instead resolves this
correctly: with one Fire connected, exact-name matching produces the
identical single result the old model-based matching did (no regression);
with two, it picks the one whose name actually corresponds to that
confirmed session. The same latent ambiguity existed for the APC MINI branch
(harmless in practice, since only one has ever been connected) and was fixed
in parallel for consistency, since it's the identical code shape a few lines
away.

`WinMmMidiOutput.GetDevices` calls native WinMM enumeration directly and
cannot be faked in a unit test, so the actual device-selection *logic* was
extracted into `MatchMidiOutputPorts` (pure, takes a device list) precisely so
it could be tested without that native dependency.

Covered by `MIDI_display_prefix_is_stripped_to_recover_the_raw_output_port_name`
and `A_confirmed_Fire_matches_only_its_own_output_port_when_two_Fires_are_connected`
(`DeviceAwareControllerRuntimeTests.cs`); the latter reproduces the owner's
exact two-Fire scenario (`"FL STUDIO FIRE"` and `"2 - FL STUDIO FIRE"` both
present) and asserts each confirmed identity resolves to only its own port.
Confirmed to fail, reproducing the "matched 2 items" ambiguity, without this
fix before being left in place passing.

## D-025 — LED animations are a shared-canvas, capability-gated extension point, not a Fire-specific feature

**Status:** Milestone 1 (single-device procedural effects, named library)
implemented 2026-09-20. The shared-canvas participant model this decision
anticipated was built out and physically confirmed 2026-09-20 — see
[D-032](#d-032--any-number-of-confirmed-led-grid-controllers-can-share-one-animations-canvas).
See [`LED_ANIMATIONS.md`](LED_ANIMATIONS.md).

**Decision:** Animation playback is driven by a device-agnostic
`ILedGridAdapter` abstraction (`Tappy.App.Services.LedGridAdapter.cs`), keyed
by the existing `MidiControllerModelCatalog` model ids and resolved once per
confirmed device (`LedGridAdapterCatalog.Find`). The saved animation document
(`Tappy.Core.Models.LedAnimationDefinition`) targets one shared logical
canvas with a `Participants` list carrying per-device column/row offsets,
rather than one document per physical device — even though only one
participant is ever populated today. The animation library is its own
top-level store (`AnimationLibraryStore`, rooted directly under Tappy's
shared data directory, atomic-write-plus-last-known-good-backup like
`AtomicProfileStore` but implemented independently so the new feature never
risks that proven, critical path), never embedded in any per-tab profile.
Hardware output reuses `DeviceAwareControllerRuntime.SyncControllerLedColors`
and its existing exact-port-name device matching (`MatchMidiOutputPorts`,
D-024) unchanged. Preview updates during playback go through the same
per-tile color application every one-off color apply already uses
(`MainViewModel.ApplyAnimationPreviewColors`, reusing the private
`ApplyTileColor`), but deliberately never call `SaveControllerLayoutAsync` —
an animation can tick up to ~15 times a second, and treating every tick as a
profile edit would turn playback into a stream of disk writes.

**Reason:** the owner's request explicitly named supporting "many other
Ableton Live controllers" architecturally, not just the Fire, and explicitly
asked for multiple physical grids to play "one pattern spanning all grids"
rather than independent mirrored copies. A Fire-specific implementation, or
one that stored a separate animation per device, would need a breaking
rewrite to reach either requirement later — building the adapter interface
and the shared-canvas participant model from the start, even though
Milestone 1 only ever has one adapter instance and one participant, avoids
that. The Fire and APC MINI adapters wrap their models' already-verified
`TryParseAkaiFirePadIndex`/`TryParseApcMiniLedNote`/`FireRgb`/
`ApcMiniVelocity` logic rather than re-deriving pad addressing, so this
introduces no new hardware-protocol claims — see `LED_ANIMATIONS.md`'s
"What's new vs. what's reused" section.

Covered by `LedAnimationModelsTests.cs`/`LedEffectMathTests.cs`
(`Tappy.Core.Tests`) and `LedGridAdapterTests.cs`/`AnimationLibraryStoreTests.cs`/
`LedAnimationPlayerTests.cs`/the `HasAnimatableLighting` theory in
`MainViewModelTests.cs` (`Tappy.App.Tests`). The "no profile write during
playback preview" behavior was confirmed to fail (a disk write was observed
between ticks) without the fix, before being left in place passing — see
`LedAnimationPlayer_ticks_update_preview_without_writing_the_profile`.

## D-026 — MIDI devices become reconnect-eligible only when a real Windows hardware identity resolves

**Status:** Implemented and physically confirmed end-to-end 2026-09-20, on the
development machine with two real Akai Fire units and a real APC MINI
connected directly (not yet re-verified on TESTBED). All three devices were
identified, confirmed, and saved; Tappy was closed from the taskbar and
relaunched; all three tabs came back fully confirmed
("Controller confirmed for this session. Only its events can reach
mappings.") with no re-identification gesture needed, exactly matching the
owner's original ask.

Two real bugs surfaced while reaching that result and were fixed along the
way, both covered by regression tests:

- `WindowsDeviceContainerIdResolver.TryReadTrailingInterfaceClass` parsed a
  device-interface path's trailing GUID to the end of the string. Raw Input
  keyboard paths end right after the closing brace, so this worked for
  keyboards, but a WinMM MIDI device's own path (obtained via
  `DRV_QUERYDEVICEINTERFACE`) carries a KS reference string after it, e.g.
  `...#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global`. `Guid.TryParse` rejects
  that trailing `\global`, so ContainerId resolution silently failed for
  *every* MIDI device, on every attempt, despite the mechanism otherwise being
  correct — confirmed via temporary diagnostic logging against the real Fire
  and APC MINI hardware, then fixed by stopping the parse at the closing
  brace instead of the end of the string. See
  `WindowsDeviceContainerIdResolverTests.cs`.
- `WinMmMidiInputProvider.CreateDescriptors` ran the (expensive) SetupAPI
  ContainerId lookup on every poll tick (500ms) for every MIDI device on every
  tab, uncached. With three real MIDI devices connected this was slow enough
  that reconnecting tabs could sit indefinitely in "waiting for reconnect."
  Fixed with a per-device-signature cache (`_containerIdCache`) plus a
  2-second bounded timeout around the native call.

**Decision:** `WinMmMidiInputProvider.CreateDescriptors` now attempts, per
device, to resolve that device's real Windows `DEVPKEY_Device_ContainerId` —
the same hardware-identity property Raw Input keyboards already use via
`WindowsDeviceContainerIdResolver` (unchanged, reused as-is). The device
interface path it needs is obtained through a new
`WinMmDeviceInterfaceQuery.TryGetMidiInDeviceInterfacePath`/
`TryGetMidiOutDeviceInterfacePath` (`Tappy.Windows/Input/WinMmDeviceInterfaceQuery.cs`),
which wraps the documented `DRV_QUERYDEVICEINTERFACESIZE`/
`DRV_QUERYDEVICEINTERFACE` messages sent via `midiInMessage`/`midiOutMessage` —
per Microsoft's own docs, "the system intercepts this message and returns the
appropriate value without sending the message to the device driver," so this
is answered directly by winmm.dll for every MIDI device, not something the
Fire's or APC MINI's own driver needs to support, and it never opens or
disturbs the MIDI port (the first message parameter is the device ID itself
cast to the handle type, not an opened handle).

When a ContainerId resolves, that device's `PersistentId` is built from it
(`winmm-midi:vid-XXXX:pid-YYYY:container-sha256-<hash>`, mirroring the
keyboard `PersistentId` format) and `IsAmbiguous` becomes `false` — making it
eligible for the *existing, unmodified* remembered-reconnect pipeline
(`DeviceAwareControllerRuntime`'s eligibility checks gate purely on
`IsAmbiguous`, with no separate MIDI-specific exclusion anywhere else in that
pipeline, confirmed by reading it before making this change). When resolution
fails for any reason — older Windows, a device with no SetupAPI-visible
interface, a thrown exception anywhere in the lookup — the device falls back
to exactly today's signature-plus-enumeration-ordinal `PersistentId` and stays
`IsAmbiguous: true`, identical to D-018's original behavior. This is a
fail-closed, strictly additive change: nothing about the previous "MIDI always
manual" behavior can regress from a resolution failure, only improve on a
resolution success.

**Reason:** the owner asked for several MIDI controllers to stay set up
across an app restart, specifically for LED-animation demos where redoing
identification for every connected controller each launch is tedious. D-018
deliberately excluded MIDI because WinMM's own device-capabilities API
(`midiInGetDevCaps`) exposes only manufacturer/product ID, driver version, and
a 32-character name — nothing resembling a hardware serial — so the "ordinal"
Tappy previously used to disambiguate two identical units was WinMM's own
enumeration order, which is not guaranteed stable across a restart. Before
implementing anything, this was researched (confirming no Windows MIDI API,
old or new, can invent identity a device doesn't report) and then verified
directly against the owner's real hardware on TESTBED: the Fire's USB instance
IDs show no real serial number (Windows synthesizes one from USB topology), so
a true "survives being moved to a different port" identity is not achievable
for these controllers on any API — but Windows was directly confirmed, via
`Get-PnpDeviceProperty -KeyName DEVPKEY_Device_ContainerId` against two
simultaneously-connected real Fire units, to already assign them two distinct,
stable ContainerId GUIDs today. That is exactly the same identity strength
keyboards already rely on (`PortBound`: trusted in the same USB port,
requiring one manual re-identification after a port change) — so extending
the *existing* keyboard identity mechanism to MIDI, rather than inventing
anything new, reaches the owner's actual goal (leave several controllers
connected, skip re-setup each launch) within the same safety tier the app
already accepts for keyboards, without weakening anything for a device where
that identity can't be resolved.

Covered by `Enumeration_resolves_a_stable_container_identity_when_available`,
`Enumeration_falls_back_to_the_ordinal_identity_when_no_interface_path_is_found`,
`Enumeration_stays_ambiguous_when_the_container_resolver_throws`, and
`Two_identical_units_get_different_container_based_identities_when_resolved`
(`WinMmMidiInputProviderTests.cs`, `Tappy.Windows.Tests`); `WinMmDeviceInterfaceQueryTests.cs`
(graceful failure for an impossible device ID — the one path testable without
real hardware); and `MIDI_identity_with_a_resolved_container_id_restores_across_app_restart`
(`RememberedReconnectTests.cs`, `Tappy.App.Tests`), which exercises the full
close/reopen cycle through `DeviceAwareControllerRuntime` end-to-end and was
confirmed to fail without this fix before being left in place passing. The
pre-existing `MIDI_identity_never_restores_across_app_restart` test (the
no-resolver fallback path) was re-verified to still pass unchanged.

Still required before promotion: re-running the same attended app-restart
cycle on TESTBED (this confirmation ran on the development machine, not
TESTBED); and a check that moving a previously-confirmed unit to a different
USB port correctly falls back to requiring manual identification rather than
silently misattaching another device's saved layout/mappings.

## D-027 — The Fire's OLED ships as a fixed diagnostic pattern first, not a drawing API

**Status:** Implemented and physically confirmed 2026-09-20: with the owner
watching a real Fire's physical screen, the diagnostic pattern was sent for
the first time and rendered perfectly on the first attempt — clean border,
correct corner marker, no flicker, no glitching, no hang or disconnect. See
"OLED" in [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** `DeviceAwareControllerRuntime.FireOledFrameSysEx`/
`FireOledBitPosition` implement the documented 128×64 monochrome SysEx write
(Sub-ID `0E`) and its non-linear pixel-to-bit tiling verbatim, following the
same static-encoder-plus-runtime-wrapper shape `FirePadColorSysEx` already
established. Rather than building any drawing surface, status-text renderer,
or content pipeline on top of it, this first pass exposes exactly one thing: a
"OLED test (experimental)…" button (visible only for a confirmed Fire,
gated the same way `HasFreeformColorSupport`/`HasAnimatableLighting` already
gate Fire-only UI) that sends one fixed, hand-picked diagnostic bitmap —
`FireOledDiagnosticPattern`: a full one-pixel border plus a solid 8×8 square
in the top-left corner only — behind an explicit Yes/No confirmation dialog
that names the destabilization risk before sending anything.

**Reason:** this is the first time Tappy has ever sent SysEx sub-ID `0E` to
real hardware, and `docs/AKAI_FIRE.md` already carried a standing warning
(from the SysEx transport's own write-up) that out-of-range SysEx values are
reported to destabilize the Fire. The pad-color work earlier this session
established the right discipline for exactly this situation: prove the
riskiest new piece — here, the packing formula and the message framing,
applied for the first time — against real hardware with the smallest,
easiest-to-diagnose possible payload before adding any real feature on top.
The diagnostic pattern's shape is deliberately chosen so a packing bug and an
orientation/mirroring bug each produce an unambiguous, different visual
failure (noise across the whole screen vs. a marker in the wrong corner),
rather than reusing the pad-color starter-palette approach of "one plausible
value" that would leave a transposed axis or scrambled tile looking merely
odd rather than obviously wrong. Text/font rendering is deliberately deferred
entirely — a font renderer bug and a packing-formula bug would otherwise be
conflated in the same first attempt.

Covered by `FireOledTests.cs` (`Tappy.App.Tests`):
`FireOledBitPosition_maps_every_pixel_to_a_unique_in_bounds_bit` exhaustively
checks all 8,192 real pixels resolve to a distinct, in-bounds
(byte index, bit) pair — internal self-consistency of this implementation of
the documented formula, not a re-derivation of the formula's own correctness,
which only real hardware can confirm;
`FireOledFrameSysEx_matches_the_documented_frame_format_for_a_blank_pattern`
and `FireOledFrameSysEx_sets_exactly_one_bit_for_a_single_lit_pixel` verify
the message's exact byte layout (header, Sub-ID, length bytes, the fixed
`00 07 00 7F` prefix, F7 terminator) and that one lit pixel toggles exactly
one bit and nothing else; `FireOledDiagnosticPattern_draws_a_border_and_a_top_left_corner_marker`
locks down the diagnostic bitmap's shape. None of this substituted for the
physical check itself — see item 7 in `AKAI_FIRE.md`'s "Required finite
validation," now done. Only the one fixed diagnostic bitmap has been tried;
arbitrary OLED content (text, status display, etc.) is still unimplemented.

## D-028 — The Fire's OLED reverts on its own; sending to multiple Fires must land back-to-back

**Status:** Implemented and physically confirmed 2026-09-20. See "OLED —
sending to multiple Fires at once" in [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** `ControllerWorkspace.SendOledTestPatternToAllFires` iterates
every tab, filters to confirmed Fires by `GetControllerLedColorCapability()?.ModelId`
(the same check `MainViewModel.HasOledDisplay` already uses), and calls each
one's existing `SendFireOledDiagnosticPattern()` in sequence with no UI
round-trip in between. Exposed as a second button, "OLED test — all Fires…",
next to the single-device one, behind a lighter confirmation dialog (the
destabilization warning does not need repeating now that D-027 physically
proved this exact write safe; the new dialog instead names the timing
behavior below so the result isn't surprising).

**Reason:** physically testing D-027's single-device feature surfaced a real,
previously-undocumented hardware behavior: the owner reported the diagnostic
pattern appeared correctly, then went dark on its own after "a few seconds,
not minutes," on both units tried. Nothing in Tappy ever sends a clear/off
command — `FireOledFrameSysEx`/`SendFireOledDiagnosticPattern` only ever
issue the one write — so this is the Fire's own firmware reverting an idle
OLED, presumably because the real OLED protocol is designed for FL Studio's
control-surface script continuously refreshing it, not a one-shot external
write. This was confirmed to matter concretely: the owner's first attempt at
seeing both Fires' screens lit together used the single-device button once
per tab (switching tabs between clicks), and the first unit's pattern had
already reverted before the second unit's send went out — they were never
lit at the same time. Landing both sends back-to-back in code, with no
per-click UI round-trip or confirmation dialog between them, closed that gap.

**Physically confirmed, 2026-09-20:** with two real Fires connected and the
owner watching both physical screens, clicking "OLED test — all Fires…" lit
both screens with the correct pattern at the same time, and both reverted to
black together a few seconds later — confirming both that the back-to-back
send lands inside the idle-revert window and that one unit's OLED write does
not interfere with the other's.

This finding is load-bearing for any future real OLED content: a status
display, bank indicator, etc. would need to be re-sent periodically to stay
visible, the same way LED animations already re-tick every ~66ms — a
single write is only ever temporary on this hardware. Not yet designed or
implemented.

Covered by `Sending_the_OLED_test_pattern_to_all_Fires_reaches_only_the_confirmed_Fire_tabs`
(`CrossControllerBankTests.cs`, `Tappy.App.Tests`): a workspace with two real
`DeviceAwareControllerRuntime` Fire tabs and one APC MINI tab (no
`WinMmMidiOutput` wired to any of them) confirms `SendOledTestPatternToAllFires`
returns exactly 2 results, both attempted-but-failed for the expected
missing-hardware-output reason, proving the APC MINI tab was filtered out
rather than silently skipped for an unrelated reason. The revert-timing
behavior itself is a hardware fact, not something a deterministic test can
exercise.

## D-029 — The Fire's OLED shows live device/layer status, kept alive by a periodic refresh

**Status:** Implemented and physically confirmed 2026-09-20. See "Live
status display" in [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** A new, original 5x7 monochrome bitmap font
(`Tappy.Core.Models.FireOledFont`, covering uppercase, lowercase, digits,
space, and `. : - / ( ) '`) and a pure text-layout function
(`FireOledTextRenderer.Render(lines, columns, rows)`) render plain strings
onto a pixel buffer; a font character outside the covered set renders as a
blank glyph rather than throwing, since content strings are free text
(layer names are user-editable). `DeviceAwareControllerRuntime.SendFireOledDiagnosticPattern()`
was refactored into a thin wrapper over a new, generalized
`SendFireOledFrame(bool[,] pixels)` — the proven diagnostic-pattern send path
is unchanged, just factored to also accept arbitrary content.

A new per-tab `FireOledContentPlayer`, ticked every ~2s by a new
`_oledContentTimer` in `MainWindow.xaml.cs` (the same per-concern-`DispatcherTimer`
ownership convention as `_reconnectTimer`/`_zoomPersistTimer`/`_animationTimer`),
renders and re-sends the confirmed Fire's device name
(`StripMidiDisplayPrefix(ActiveControllerLabel)`) and active layer name
(`ActiveLayerName`) — both already-existing, already-live `MainViewModel`
properties, so no new state plumbing was needed. The player is deliberately
stateless about "am I showing": it re-reads a new
`MainViewModel.ShowLiveStatusOnOled` bool (bound to a new "Show live status
on OLED" checkbox, default off) on every tick rather than tracking its own
started/stopped flag, so the checkbox can never drift out of sync with what
is actually being sent. The hardware push runs off the UI thread via
`Task.Run` with a single-flight guard, mirroring `LedAnimationPlayer`'s
proven shape exactly (a slow send skips intermediate ticks rather than
queuing stale frames).

**Reason:** physically testing D-027/D-028 established that the Fire's OLED
reverts to black on its own a few seconds after its last update — a fact
that shapes this whole design. Showing *anything* persistently therefore
requires a re-send loop, not a one-shot render, the same way LED pad
animations already re-tick continuously. The opt-in checkbox (rather than
automatically taking over a confirmed Fire's screen) exists because this is
the first continuous, unattended, repeating SysEx write pattern anywhere in
Tappy — every other Fire lighting/OLED action so far has only ever sent on a
single deliberate user click. The font was hand-authored as readable
ASCII-art source (7 rows of `#`/`.` per glyph) rather than opaque byte
tables, both so mistakes are easy to spot in a diff and so no copyright
question about a reused third-party font table could arise; every glyph and
three representative device/layer strings were rendered at the real 128x64
scale via the Artifact/visualize tool and visually confirmed correct before
anything touched hardware.

**Physically confirmed, 2026-09-20**, on the development machine with the
owner watching both real Fires throughout:
- A single confirmed Fire, checkbox enabled: device name and active layer
  rendered correctly on the first attempt, with no font/glyph errors.
- The display stayed lit and correct for over 40 seconds without going
  dark, confirming the periodic refresh actually holds it visible past the
  hardware's own few-second revert window.
- Switching the active layer while the checkbox stayed checked updated the
  second line within one refresh tick, with no other side effects.
- Unchecking the box was followed by the screen going dark on its own within
  about 10 seconds — confirming Tappy sends no explicit clear command and
  the hardware's own idle-revert (D-028) is what actually blanks it.
- Both Fires with the checkbox enabled simultaneously showed their own
  correct, independent device name and layer at the same time, matching
  D-028's back-to-back-send finding: `ControllerWorkspace.TickOledContent()`
  iterates every tab's player on the same timer tick, so neither unit's
  refresh starves the other's.

Covered by `FireOledFontTests.cs`/`FireOledTextRendererTests.cs`
(`Tappy.Core.Tests`: every declared glyph is 5x7 with at least one lit pixel,
an unmapped character falls back to blank without throwing, rendered text
matches its glyphs exactly at the expected position, overflow truncates
rather than wraps, a line beyond the given height is dropped, empty input
produces an all-blank buffer) and `FireOledContentPlayerTests.cs`
(`Tappy.App.Tests`: `BuildStatusLines` returns the stripped device name and
active layer for a real confirmed Fire; `Tick` does not throw when not
opted in, when the confirmed device isn't a Fire, or when opted in with no
real hardware output wired). The periodic-refresh/persistence and
simultaneous-multi-Fire behaviors are hardware facts, not something a
deterministic test can exercise, matching D-028's precedent.

## D-030 — The OLED gained a mode selector (logo, scrolling wordmark) and a one-time welcome sequence

**Status:** Implemented and physically confirmed 2026-09-20. See "Live
status display, Tappy logo, and scrolling wordmark" in
[`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** `MainViewModel.ShowLiveStatusOnOled` (a bool) was replaced by
`FireOledDisplayMode` (Off / LiveStatus / TappyLogo / ScrollingWordmark),
selected by four buttons instead of one checkbox. `FireOledContentPlayer`
stayed deliberately stateless about which mode is active — it re-reads the
property every tick — but gained real state for the one thing that
genuinely needs it: a one-time "welcome sequence" (logo, then the scrolling
wordmark, then whatever mode was selected) that plays the first time a
tab's Fire becomes confirmed each running session, per the owner's request
("when the program is opened or a Fire controller is plugged in and
recognized... for the first time in a session"). `Tappy.Core.Models.FireOledLogo`
(a packed 128x64 bitmap converted from the app's own icon) and
`FireOledTextRenderer.RenderScaledLine`/`Outline` (a large, hollow-stroked
"TerkWerX") are new, reusable Core-layer pieces alongside the existing font
renderer; `FireOledScroll.ExtractWindow` remains the one general-purpose
windowing primitive for both this wordmark and any future scrolling
content. The shared OLED timer moved from 2s to 150ms so the scroll reads
as smooth motion, not a slideshow — a static mode simply tolerates the
faster cadence without needing it.

**Reason:** the owner asked for exactly two content types beyond live
status — a logo bitmap and a large scrolling wordmark — then, watching them
physically, asked for the automatic first-connect welcome behavior, for the
letters to be "fat" (filling the OLED's height, not just scaled up
modestly), and then for them to be hollow rather than solid once fat
letters made that legible. Each of those was a real, iterative
hardware-driven adjustment, not a first-guess design: the scroll speed was
tuned up 3-4x after the owner reported it only reached the "W" before the
welcome's scroll phase ended, and a genuine bug (a disconnect mid-welcome
leaving its clock running, so a quick reconnect resumed a stale partial
welcome instead of treating it as already played) and a rough edge (the
scroll settling mid-way into a second partial repeat depending on exact
tick timing, reported as "paused... with a Te") were both found and fixed
from real observation on real hardware, not anticipated in advance.

Covered by `FireOledLogoTests.cs` (`Tappy.Core.Tests`: exact dimensions, an
exact lit-pixel-count regression lock on the hand-authored packed bitmap,
blank margins on every edge), `RenderScaledLine`/`Outline` additions to
`FireOledTextRendererTests.cs` (scale-1 matches the plain font exactly,
every dimension scales by the given factor, a large solid block's outline
keeps only its border and clears the true interior at the requested
thickness, a shape thinner than the stroke stays fully lit, an all-blank
source stays blank), `FireOledScrollTests.cs` (zero-offset returns content
verbatim, positive/negative offsets shift and wrap correctly, the gap stays
blank between repeats, a wide window tiles multiple repeats, empty content
with no gap is all-blank), and new welcome-sequence coverage in
`FireOledContentPlayerTests.cs` — including
`FireOledContentPlayer_welcome_sequence_never_replays_after_a_reconnect_in_the_same_session`,
which was confirmed to fail (returning the logo again) before the
disconnect-clears-the-welcome-clock fix, and pass after. `Tick` was
refactored to take an explicit `DateTimeOffset now` parameter (matching
`LedAnimationPlayer`'s existing shape) specifically so the welcome
sequence's phase transitions could be tested deterministically rather than
depending on real elapsed wall-clock time.

## D-031 — The Fire gets 4 banks by default and its Modes/Bank LED follows Tappy's active bank

**Status:** Implemented and physically confirmed 2026-09-20. See
"Modes/Bank indicator" in [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** The Fire's `defaultLayerCount` changed from the generic 3
(shared with every other non-Tartarus MIDI controller) to 4, matching its
own physical Modes/Bank indicator's four positions (Channel Rack / Mixer /
User 1 / User 2). An existing saved Fire profile with only 3 banks gains a
4th on its next confirm, mirroring the Tartarus 8-layer retroactive-upgrade
pattern already established in the same method — matched by layer Id, so a
user-renamed bank is never disturbed. A new `DeviceAwareControllerRuntime.SyncFireBankIndicatorLocked`
sends CC `1B` (channel 1, value = the 0-based active bank index) whenever
`ChangeBank` changes a confirmed Fire's active bank, so a physical button
mapped to "next bank" visibly moves the Fire's own hardware LED, not just
Tappy's internal state. This resolves the CC `1B` encoding question
`AKAI_FIRE.md` had carried as an open, unverified discrepancy since the
Fire's lighting protocol was first researched: two candidate readings
existed (a plain 0-based index vs. a `0x10`-based bitmask), neither
confirmed against real hardware by either source. The plain-index reading
was implemented first (the more directly actionable of the two, from the
cross-check project's own — likewise physically-unverified — implementation)
and, on the very first attended test, turned out correct.

**Reason:** the owner's own physical setup already has the Fire's Modes/Bank
button mapped to Tappy's "next bank" control (visible in a screenshot of
their live workspace), and asked directly for two things: 4 banks instead
of 3, and the physical LED to move with the button the way it does on real
hardware running FL Studio. Sending is best-effort and never blocks the
bank change itself (matching every other Fire lighting sync's fail-soft
precedent) since a bank change is the thing that must always succeed even
if the confirmed device's output port cannot be resolved for some reason.

**Physically confirmed, 2026-09-20:** the owner pressed the Fire's own
physical Modes/Bank button (mapped to "next bank") and confirmed both that
Tappy's active bank changed and that the physical LED moved to match, on
the first attempt.

Covered by `FireBankIndicatorTests.cs` (`Tappy.App.Tests`):
`Confirming_a_new_Fire_creates_four_banks_by_default`;
`An_existing_saved_Fire_profile_with_three_banks_gains_a_fourth_on_reconfirm`
(mirrors the existing stale-layout-upgrade test's approach — truncate a
saved profile to 3 banks, reconfirm, and check the 4th appears while a
user-renamed 2nd bank survives untouched); and
`ChangeBank_on_a_confirmed_Fire_advances_through_all_four_banks_without_throwing`,
which also exercises `SyncFireBankIndicatorLocked`'s no-real-output-wired
path. The CC value's real-hardware meaning (which of the two candidate
encodings is correct) is a hardware fact no deterministic test can
exercise, matching this project's precedent for every other previously-open
Fire protocol question.

## D-032 — Any number of confirmed LED-grid controllers can share one animation's canvas

**Status:** Implemented and physically confirmed 2026-09-20. See
[`LED_ANIMATIONS.md`](LED_ANIMATIONS.md).

**Decision:** The "Animations…" window gained a Participants section: a
checkbox per confirmed tab with a registered `ILedGridAdapter`
(`ControllerWorkspace.GetAnimationTargets()`, modeled directly on the
existing `GetBankTargets()` — same confirmed-tab, same-identity dedup rule).
Checking a device appends it to `LedAnimationDefinition.Participants` at the
next open X offset (`LedAnimationParticipantLayout.Rearrange`, pure
left-to-right layout: offset = sum of every already-checked participant's
adapter width, single row); unchecking removes it and re-flows the rest from
scratch. `CanvasColumns`/`CanvasRows` are recomputed the same way, so
`LedEffectMath.Evaluate`'s existing column/row-relative math naturally
produces one wave/chase that continues across the combined width — no
changes needed inside `LedEffectMath` itself. `LedAnimationPlayer.Tick`
re-resolves every participant against `GetAnimationTargets()` on every tick
(mirrors `RouteBankControlAsync`'s re-validate-before-every-write rule,
D-024's multi-device precedent): a participant that disconnects mid-play is
dropped from that tick only, the remaining participants keep animating, and
each participant gets its own single-flight hardware-send guard so one slow
physical write never stalls the others. The Participants checkbox list
itself is device-agnostic — it lists whatever `GetAnimationTargets()`
returns, which already included the confirmed APC MINI tab alongside both
Fires with no APC MINI-specific code added anywhere in this feature.

**Reason:** the owner circled the Fire's own Bank button and separately
asked, after the theming fixes, for checkboxes to "send animations to all or
some of the fire controllers from this window so they can share the
animation not just run it alone," explicitly choosing one continuous shared
canvas over identical mirrored playback when asked directly, and explicitly
named wanting other LED-grid controllers to "join the party" later. Milestone
1 (D-025) already modeled `Participants`/`CanvasColumns`/`CanvasRows` for
exactly this, populated with one entry; this decision is that model's first
real use, built as a generic participant list rather than a Fire-specific
"second Fire" special case so the later APC MINI (and any future
`ILedGridAdapter`) request needs no rework.

**Physically confirmed, 2026-09-20:** with both real Fires and the real APC
MINI confirmed in Tappy, the owner checked all three as participants in the
Animations window, played Rainbow Wave, and confirmed it — unprompted,
having only been asked to verify the two Fires — reported the APC MINI
joined the shared canvas correctly too. Uncheck/re-check-mid-play and Stop
behavior were exercised by the automated tests below but not separately
re-confirmed by hand in this session.

Covered by `LedAnimationMultiDeviceTests.cs` (`Tappy.App.Tests`):
`GetAnimationTargets_returns_only_confirmed_devices_with_a_registered_grid_adapter`,
`GetAnimationTargets_is_empty_when_nothing_is_confirmed`,
`Rearrange_lays_out_participants_left_to_right_and_sizes_the_shared_canvas`,
`Rearrange_reflows_remaining_participants_after_one_is_removed`,
`Rearrange_drops_a_participant_that_no_longer_resolves`,
`Shared_canvas_participants_each_render_their_own_slice_not_a_mirrored_copy`,
and `A_disconnected_participant_is_dropped_from_the_tick_but_the_other_keeps_animating`.
The two-fake-Fire fixture initially collided on `PersistentId` (both fell
through to the same real-hardware container-id resolution instead of the
isolated fakes every other multi-device test in this suite already injects)
and was confirmed failing (`GetAnimationTargets()` returned 0 instead of 2)
before the fixture fix, matching this project's fail-without-fix/pass-with-fix
discipline.

## D-033 — The Fire's 5 encoders gained a measured photo hotspot, closing the last button/pad/encoder locator gap

**Status:** Implemented 2026-09-20, not yet physically confirmed (a real
encoder needs to be turned on the physical unit to confirm the rotating
indicator itself). See [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** `ControllerPhotoCatalog.CreateAkaiFireV1()` gained 15 new
hotspot entries — one per encoder control (clockwise, counter-clockwise,
touch) across the 5 knobs (Volume, Pan, Filter, Resonance, Select) — bringing
the Fire photo's hotspot count from 84 to 99, exactly matching
`MidiControllerModelCatalog`'s 99-control Fire model for the first time.
Each knob's three controls share one circular (`ControllerPhotoHotspotShape.Ellipse`)
visual spot via a common `VisualId`: the clockwise control is the canonical
entry the other two point back to, the same alias pattern the Targus
NumLock/Enter hotspots already use elsewhere in the same file. Both rotation
directions are `ControllerPhotoControlKind.RotaryEncoder`; touch is `Button`.
No `Minimum`/`MaximumIndicatorLeft`/`Top` or `RotaryStartAngle`/`RotarySweepAngle`
fields are set, because those only feed the *calibrated, absolute-position*
angle formula `RotaryEncoder` deliberately bypasses — its `IndicatorAngle`
reads `ApplyEncoderDelta`'s accumulated angle directly, matching the Fire's
encoders being relative/endless with no known absolute position (see D-023's
`AnalogRawValue` rule). This activates a `RotaryEncoder` visual-feedback code
path that has existed since before any model used it (per `AKAI_FIRE.md`'s
own prior note: "built earlier, previously unused by any model") — this is
its first real activation.

Knob centers were found the same way the existing 84 button/pad hotspots
document they were measured (`CreateAkaiFireV1`'s own doc comment: "column/row
brightness-peak detection... visual measurement"): the four main knobs' centers
came from a column-brightness-peak scan of the `VOLUME`/`PAN`/`LOW EQ`/`HIGH EQ`
header text sitting directly above each one (reliable high-contrast
white-on-black text; the knobs' own metal-gradient silhouette was too
low-contrast against the near-black chassis for the same technique to work
directly on the knob), cross-checked against the pad grid's already-measured
column spacing for a second independent estimate. The Select knob's position
was not derivable from header text (it has none) and an initial guess placed
it on the wrong control (the Grid Left/Right button cluster) entirely; it was
corrected by direct visual inspection. All five centers were then confirmed
by drawing a candidate circle directly onto the photo and checking it against
the actual knob/silhouette, iterating once to tighten the fit.

**Reason:** this was the last unfinished piece of the Fire's photo locator —
`AKAI_FIRE.md` and `COMPATIBILITY.md` both explicitly tracked it as an open
gap ("the five encoders do not have measured photo hotspots yet... a
follow-up") ever since the encoders were added to the input model. Buttons,
pads, and encoders all being assignable but only two of the three having a
visual locator was an inconsistent, easily-forgotten gap the owner asked to
see closed.

**Verified in the running app, 2026-09-20:** selecting an encoder's rotate
tile (checked on Volume, the first knob and the one whose center came from
the header-text technique, and Select, the last knob and the one whose
position required correction and independent visual placement — the two
most different derivations among the five) visibly highlights the correct
knob on the photo locator panel, confirmed via screenshots of the live app
with a real Fire confirmed in the tab. Turning a real encoder to confirm the
rotating indicator itself (as opposed to just the static selection highlight)
remains a physical check for the owner.

Covered by `ControllerPhotoCatalogTests.cs` (`Tappy.App.Tests`):
`Exact_Fire_name_has_one_bounded_hotspot_per_known_control` (renamed and
strengthened from the prior 84-only assertion to require an exact 1:1 match
between the photo's hotspots and all 99 model controls, mirroring the G13 and
APC MINI tests' existing coverage pattern) and
`Fire_encoders_share_one_rotary_visual_spot_across_touch_and_both_rotation_directions`
(new — proves all 10 rotation-direction hotspots group into exactly 5 shared
visual spots of 2 each, every control at a shared spot has identical
geometry, the touch control at each spot is `Button` kind sharing the same
`VisualId`, and no Minimum/MaximumIndicator fields are set on any of them).

## D-034 — The frame-by-frame editor (Milestone 2) is implemented and physically confirmed

**Status:** Implemented and physically confirmed 2026-09-21. See
[`LED_ANIMATIONS.md`](LED_ANIMATIONS.md).

**Decision:** Milestone 2 closes the last gap in the LED Animations feature —
the "Frames" mode radio button, disabled with a "(coming soon)" tooltip since
D-025, is now a full hand-painted frame editor. The underlying data model
(`LedAnimationKind.Frames`, `LedAnimationFrame`'s `DurationMs`/`Cells`, and
their `Normalize()`/`Clone()`/snapshot round-trip) needed no changes at all —
it was built ahead of time in Milestone 1, exactly like `Participants` was
before D-032 used it. The real work was two pieces:

1. A new pure, deterministic `Tappy.Core.Models.LedFramePlayback.GetActiveFrameIndex`,
   styled like `LedEffectMath`, replacing `LedAnimationPlayer.Tick`'s literal
   `// Frame playback lands in Milestone 2.` placeholder that had rendered
   solid black for any Frames-kind animation until now. `Play()` gained a
   matching refusal for a Frames-kind animation with zero frames, alongside
   the existing no-resolvable-participant refusal.
2. A Frames-mode panel in `LedAnimationWindow` — a frame timeline
   (Add/Duplicate/Delete/Move up/Move down, each frame's own editable
   duration) beside a paintable canvas sized to the animation's existing
   `CanvasColumns × CanvasRows` and a paint-color palette reusing
   `MainViewModel.AllTileColorChoices`/`BuildCustomColorChoice` (promoted from
   `private` to `internal static` — one color table, not a duplicate) plus the
   existing `FireColorPickerWindow` for arbitrary hex. A Frames-kind animation
   is never left with zero frames, enforced centrally in
   `LedAnimationEditorViewModel.RefreshFrameChoices()` rather than only on the
   live mode-switch action, so it self-heals for an animation loaded from disk
   with `Kind: Frames` and no frames too — a real gap the first pass missed,
   caught by watching the running app rather than by the unit tests alone
   (every unit test exercised the mode-switch path, none exercised "already
   `Kind: Frames` at construction time").

Building this surfaced a second, unrelated but real bug: `LedAnimationWindow`'s
"Library" list (present since Milestone 1) and the new Frames timeline list
both use `ListBox` bound via `DisplayMemberPath`. This is the same class of
bug already fixed this session for the window's data-bound Effect `ComboBox`
(`Every_data_bound_combo_box_has_an_item_template` in `ThemeResourceTests.cs` —
a smaller UI fix that predates this decision and was never given its own
numbered entry): the auto-generated item content does not reliably pick up
the themed `ListBoxItem` style's `Foreground` setter. It applies to `ListBox`
too: item
text rendered in the app-wide light `TextBrush` (`#F3F7FA`) against
`AssignmentListBackgroundBrush`'s deliberately white background
(`#FFFFFF`), i.e. invisible white-on-white text. This had apparently gone
unnoticed in the pre-existing Library list since the window was narrower and
less scrutinized before this session; clicking a row still selected it (a
visible blue highlight bar), just never showed a label. Fixed the same way
the ComboBox case was: an explicit `ItemTemplate` with a `TextBlock` setting
`Foreground="{DynamicResource AssignmentListTextBrush}"` directly, on both
lists.

**Reason:** the owner asked to pick up work on the frame-by-frame editor
after the Fire encoder locator (D-033), the other remaining item from an
earlier "what's not finished" audit of the whole project. The auto-add and
"never zero frames" design choices mirror `NewAnimationDefaultingToThisTab`'s
established "sensible default, no dead-end" convention; the shared color
palette avoids a second, potentially-drifting copy of the same color table;
painting "Off / default" removing rather than storing the dictionary entry
matches the sparse-storage convention the model's own `Cells` dictionary
comment already implies.

**Physically confirmed, 2026-09-21:** with a real Fire confirmed, the owner
watched a two-frame animation (frame 1: red and blue pads painted, ~2.7s;
frame 2: a single green pad, ~2.7s) play on the real hardware via the app's
live preview — Play visibly cycled from the frame 1 pattern to the frame 2
pattern at the expected timing, Stop turned every pad off rather than
freezing the last frame lit, and closing and reopening the Animations window
preserved the painted frames, their durations, and the Participants
selection unchanged.

Covered by `tests/Tappy.Core.Tests/LedFramePlaybackTests.cs` (empty list,
single frame, multi-frame boundary timing, looping wraparound, non-looping
clamp-to-last), `LedAnimationPlayerTests.cs`'s two new Frames-kind cases
(active-frame colors show correctly including an unpainted cell falling back
to Default; `Play()` refuses an empty Frames-kind animation), the new
`LedAnimationFrameEditorTests.cs` (Add/Duplicate/Delete/Move ordering and
selection, the "never zero frames" self-heal on both the mode-switch and the
loaded-with-zero-frames paths, `PaintCell` setting and erasing, custom hex
colors), and `ThemeResourceTests.cs`'s new
`Every_data_bound_list_box_sets_its_item_text_foreground_explicitly` (which
would have caught the ListBox text bug directly, had it existed before this
session).

## D-035 — The Fire's Mute/Solo photo hotspot moved onto the actual circular button

**Status:** Implemented 2026-09-21, resolving an open question `AKAI_FIRE.md`
carried since the button/pad photo hotspots were first measured. See
"Mute/Solo" in [`AKAI_FIRE.md`](AKAI_FIRE.md).

**Decision:** `ControllerPhotoCatalog.CreateAkaiFireV1()`'s four Mute/Solo
hotspots moved from a rounded-rectangle centered at x=137 to a circle
(`ControllerPhotoHotspotShape.Ellipse`) centered at x≈78, radius 23 — the
measured center and radius of the button's visible ring on the photo. The
photo shows two distinct elements at this position on the real unit: a round
button (the actual Mute/Solo control, note `24`-`27`) and, separately to its
right (photo x≈130-145), a small rectangle that is one of the Fire's four
bi-color indicator LEDs (CC `28`-`2B`, already documented as a
protocol-level open question in D-021's era of `AKAI_FIRE.md`). The old x=137
position sat on the rectangle, not the button.

**Reason:** the owner reported, from direct physical inspection of the real
Fire, that pressing a Mute/Solo button visibly illuminates the button itself,
not the small rectangle beside it — the opposite of what `AKAI_FIRE.md`'s
own physical-testing checklist had guessed ("the rectangular element, not
the adjacent small circular indicator") before any real unit confirmed
either. Tappy's selection/illumination highlight was rendering at the old
hotspot's position, i.e. over the LED rather than the button, contradicting
what the owner sees happen on the hardware. This also surfaces a genuine,
still-open protocol question worth recording precisely rather than guessing
further: which of CC `28`-`2B` drives which of the four LEDs, and what they
indicate, remains unconfirmed — nothing in Tappy currently sends those CCs.

Covered by the existing `Exact_Fire_name_has_one_bounded_hotspot_per_known_control`
test in `ControllerPhotoCatalogTests.cs`, which already asserts every hotspot
stays within photo bounds and (via its no-overlap check across distinct
visual spots) that the moved circle does not collide with any neighboring
control; no new test was needed since the assertion shape (bounded,
non-overlapping) already covers a repositioned hotspot correctly. The exact
button/LED identity swap itself is a real-hardware fact, not something a
deterministic test can independently verify — it rests on the owner's direct
inspection, the same evidentiary standard this project applies to every
other physically-reported Fire fact.

## D-036 — Layout lock/fit-to-view/pan, and tiles shaped like the real physical control

**Status:** Implemented and physically confirmed 2026-09-21.

**Decision:** Two related workspace-editing features landed together:

1. **Lock/fit/pan.** `MainViewModel.IsLayoutLocked` — a new per-controller,
   persisted bool threaded through `ControllerLayoutWorkspace`
   (`Runtime/IControllerRuntime.cs`) and `ControllerLayoutDefinition`
   (`Tappy.Core.Models.ProfileModels.cs`) the exact same way `SnapToGrid`
   already is, including the D-022 stale-shape-reset path (preserved when
   the saved shape still matches the canonical one, reset to unlocked
   otherwise — a shape reset is exactly when a layout needs to be editable
   again, not protected). A new "Lock layout" checkbox sits next to "Snap to
   grid and neighbors". While locked: the tile `Button`'s `Cursor` switches
   from `SizeAll` to `Arrow` (a `DataTrigger` on the existing per-tile
   `Style`), `ControlTile_OnPreviewMouseLeftButtonDown` and
   `ResizeTile_OnDragStarted`/`OnDragDelta` (`MainWindow.xaml.cs`) no-op
   instead of starting a drag/resize, and the resize `Thumb`'s existing
   `IsSelected`-gated visibility trigger gained a second, `IsLayoutLocked ==
   false` condition (`MultiDataTrigger`) so it never appears while locked.
   Selection, group-select, and color actions are untouched — only
   moving/resizing is gated. A new "Fit to view" button
   (`FitControllerLayoutToViewport`) reuses `HandleControllerLayoutZoom`'s
   exact scale/scroll/debounced-save mechanics, computing the zoom that fits
   `LayoutSurfaceWidth`/`Height` into the current viewport instead of a
   wheel-driven increment. `ControllerWorkspace_OnPreviewMouseLeftButtonDown`/
   `OnPreviewMouseMove`/`OnPreviewMouseLeftButtonUp` gained a locked-only
   branch: dragging empty canvas space pans the `ScrollViewer` (capture,
   track the pointer delta, scroll) instead of starting the existing
   marquee-select, which keeps working exactly as before whenever unlocked.
2. **Physically-shaped tiles.** `ControlTileViewModel.CornerRadius` (default
   `2`, matching `App.xaml`'s shared `Button` template's existing hardcoded
   corner radius — visually unchanged for every tile without photo shape
   data) is set in `MainViewModel.EnsurePhotoHotspot` — already the one
   place that looks up `_activeControllerPhoto.Hotspots[tile.ControlId]` per
   tile — to `999` when that hotspot's `Shape` is `Ellipse`, reusing the
   photo locator's own measured shape data outright rather than authoring a
   second shape table (WPF's `Border` clamps an over-large `CornerRadius` to
   the largest valid rounding for the box, so `999` renders as a full
   circle/pill regardless of the tile's size, including after a later
   resize, without needing to recompute per resize). The workspace tile's
   `Button` gained its own small `ControlTemplate` override, scoped to just
   that one `DataTemplate` (copied from `App.xaml`'s shared template but
   with `CornerRadius="{Binding CornerRadius}"` instead of the hardcoded
   `2`) — every other button in the app still uses the unmodified global
   template. The same idea extends to the LED Animations frame-editor canvas
   built earlier today (D-034): `ILedGridAdapter.PadAspectRatio` (new)
   returns each model's measured pad width÷height from
   `ControllerPhotoCatalog` — the Fire's 58×81 (≈0.72, portrait) and the APC
   MINI's 104×50 (≈2.08, landscape) — and `FrameCellViewModel.AspectRatio`
   is set per cell in `RefreshCanvasCells()` from whichever participant's
   column range that cell falls in (a per-participant constant, not new
   per-cell work, since the canvas only ever shows grid pads, never
   buttons/knobs). A cell's rendered width comes from a new
   `FrameCellAspectRatioToWidthConverter` at a fixed base height, so a
   Fire's slice of a shared canvas visibly reads as narrow portrait pads
   right next to an APC MINI's wider landscape ones.

**Reason:** the owner asked for the lock/fit/pan workflow directly ("when a
user has a grid the way they want it, allow it to be locked... so the
cursor doesn't turn into the quad arrow... [and] allow click grab to move
around if the user is already zoomed in"), and separately asked for tiles
shaped like their physical counterparts ("redesign the shapes of the grid
boxes to be more representative of their physical shapes, like knobs versus
buttons, circles and rectangles"), then explicitly asked for that same
shaping to carry into the LED Animations canvas so "painting" a frame
visually resembles the physical grid being programmed. Diagnosing the
Mute/Solo hotspot bug (D-035) immediately before this happened to surface
exactly how much per-control photo shape data already existed to reuse for
the tile-shape half of this work, at zero new authoring cost for the Fire or
APC MINI.

**Physically confirmed, 2026-09-21:** with a real Fire confirmed, the owner
watched the workspace render the five encoders and the four Mute/Solo
buttons as circles while every pad and mode button stayed rectangular;
checked "Lock layout" and confirmed dragging a tile no longer moved it, the
resize thumb no longer appeared, and dragging empty canvas space panned the
view (scrolling to reveal previously off-screen pad columns) instead of
marquee-selecting; clicked "Fit to view" from a panned/zoomed state and
confirmed the entire layout — including the fifth "Select" encoder, not
visible before — came back into one view; unchecked the lock and confirmed
a tile could be dragged normally again. Separately, with the LED Animations
canvas showing two Fires and the APC MINI as participants, confirmed the
Fire columns rendered as narrow/tall pads and the APC MINI's columns
rendered as visibly wider/landscape pads, side by side on the same shared
canvas.

Covered by `MainViewModelTests.cs`'s extended
`Controller_workspace_zoom_and_photo_pane_width_are_per_controller_not_per_window`
(IsLayoutLocked round-trips through save/restore/confirm exactly like
Zoom/PhotoPaneWidth) and new
`Fire_tiles_get_a_circular_corner_radius_for_round_controls_and_stay_rectangular_for_pads`;
`LedGridAdapterTests.cs`'s new `FireAdapterPadAspectRatioMatchesItsOwnMeasuredPhotoHotspot`/
`ApcMiniAdapterPadAspectRatioMatchesItsOwnMeasuredPhotoHotspot` (cross-checking
the adapter's constant against the photo catalog's own independently-measured
hotspot, so the two can't silently drift apart); and
`LedAnimationFrameEditorTests.cs`'s new
`RefreshCanvasCells_assigns_each_participants_own_pad_aspect_ratio_to_its_slice_of_the_canvas`
(a real Fire+APC MINI mixed workspace, confirming each participant's canvas
slice gets its own adapter's aspect ratio, not one uniform shape). The
cursor/pan/fit interaction itself has no automated coverage — this
codebase has no existing precedent for testing WPF cursor state or
`ScrollViewer` scrolling from code-behind — and rests on the physical
confirmation above instead.

## D-037 — Workspace tiles sized to the real control's measured proportions, not just its corner rounding

**Status:** Implemented and physically confirmed 2026-09-21.

**Decision:** D-036 gave a tile the right corner rounding (circle vs.
rectangle) but left every tile at the same flat `DefaultTileWidth`×
`DefaultTileHeight` (134×106) footprint regardless of what the real control
looks like — a Fire encoder knob and a Fire pad rendered as the same-size
box, just one with rounder corners. `MainViewModel.NaturalTileSize(tile)`
(new) returns the real measured photo hotspot's width/height scaled up by a
new `PhotoShapeTileScale` (1.5, chosen to make tiles read as legitimately
bigger without dwarfing the workspace) instead of the flat default, but only
when the currently confirmed device is a grid-style LED controller —
reusing the existing `HasAnimatableLighting` check (`_controllerLedCapability`
resolves via `LedGridAdapterCatalog.Find`) as the exact scoping condition.
Every placement call site that used to read `DefaultTileWidth`/`Height`
directly (`ApplyControlPlacement`'s two branches, `ApplyControllerLayoutToRuntime`'s
save-time multiplier calculation, `ArrangeLayoutAsGridAsync`) now calls
`NaturalTileSize` instead; a saved placement's `Width`/`Height` stay a
*multiplier* against this natural size exactly as before D-036, so an
untouched tile's saved `1.0x` automatically follows the new bigger shape and
a tile the owner explicitly resized keeps its own proportion of it.
`LayoutCellWidth`/`LayoutCellHeight` (the default/auto-arrange grid pitch)
went from 144×116 to 170×170 — just past the largest natural tile any
grid-style controller currently places edge-to-edge with no gap column (the
Fire's encoders, at 52×2×1.5 = 156px diameter) — so the default and
"Arrange all as grid" layouts stay overlap-free now that tiles can be
noticeably larger than the old flat default.

The scoping to `HasAnimatableLighting` specifically (not "has any photo
hotspot data") matters: Targus and the Microsoft Number Pad also carry real
measured photo hotspots, but theirs describe a tightly-packed keyboard, not
a spaced-out pad grid — scaling their tiles the same way made their default
layout overlap. Gating on "is this a grid-style LED controller" keeps them
at the plain flat default, unaffected.

**Reason:** the owner's D-036 request asked for tiles "more representative
of their physical shapes"; after seeing the first pass (shape only, flat
size), they clarified further — "I need the grid buttons to be the same
shape as the actual controller, look at the photos to see the shape I want
the same ratio but larger" — i.e. the real width:height proportions need to
drive tile *size*, not just corner rounding, scaled up from the old flat
footprint.

**Physically confirmed, 2026-09-21:** with a real Fire confirmed, the
workspace's five encoders render as large circles visibly bigger than the
Mute/Solo circles, which are themselves bigger than a pad's rounded-corner
box; the 64 pads render as tall/portrait rectangles matching the real
panel's "on end" pads, all edge-to-edge without overlapping. With a real
APC MINI confirmed and "Arrange all as grid" used to refresh a dev profile's
pre-D-037 tile sizes, the 64 pads render as wide/landscape rectangles
matching the real panel's "alongside" pads. A saved placement's multiplier
is intentionally sticky across this change — a tile sized before D-037
keeps its old multiplier applied to the new, larger natural size, which is
why the dev profile above needed one "Arrange all as grid" click to adopt
the new proportions; a controller confirmed for the first time gets them
immediately with no such step.

Covered by `MainViewModelTests.cs`'s new
`Grid_style_controllers_get_photo_shaped_tile_sizes_matching_their_real_proportions`
(a freshly-confirmed Fire encoder measures 156×156, a freshly-confirmed APC
MINI pad measures 156×75 — both derived straight from each model's own
`ControllerPhotoCatalog` hotspot × `PhotoShapeTileScale`) and the existing
`Akai_Fire_default_layout_mirrors_physical_shape_without_overlap`, updated
to read the grid pitch from `MainViewModel.LayoutCellWidth`/`LayoutCellHeight`
instead of a hardcoded `144`/`116` so it stays correct if the pitch changes
again. The Targus and Microsoft Number Pad overlap-regression tests
(`TargusLayoutTests.cs`, `MidiControllerModelCatalogTests.cs`) already
covered the scoping requirement and caught the initial unscoped version of
this change before it shipped.

## D-038 — Compact concentric-ring encoder tiles

**Status:** Implemented and physically confirmed 2026-09-21.

**Decision:** D-037 sized a Fire encoder's tile to its real 104px measured
diameter (×1.5 scale = 156px), but an encoder is really three separate
logical controls — rotate-increase, touch, rotate-decrease — each
previously its own full-size circle sitting side by side, costing roughly
470px of horizontal workspace per knob. `ControlTileViewModel` gained
`ClusterId`, an `EncoderRingRole` enum (`None`/`Center`/`IncreaseHalf`/
`DecreaseHalf`), and a `RingGlyph` ("▲"/"▼"). `MainViewModel` derives all
three from a new `TryGetEncoderClusterRole` helper that reuses
`ControllerPhotoCatalog`'s existing `VisualId` aliasing with no new
authoring: the clockwise/increase hotspot is the canonical entry (`Kind =
RotaryEncoder`, `EffectiveVisualId == ControlId`); the counter-clockwise/
decrease hotspot (`Kind = RotaryEncoder`) and the touch hotspot (`Kind =
Button`) both point their `VisualId` back at it. The three cluster tiles
are always positioned and sized identically (`NaturalTileSize` applies a
new `EncoderClusterTileScale = 1.0` — the raw measured diameter, not scaled
up further, since three controls already share one compact ring) and
`MidiControllerModelCatalog.CreateAkaiFireV1()`'s default layout collapses
each encoder's three controls onto one shared column (stride 1 instead of
3) so a freshly-confirmed Fire starts with them already stacked.

Three new `ControlTemplate`s in `MainWindow.xaml` render the cluster: the
center is a small `Ellipse` inset from the tile's bounds; each half is a
full-diameter `Ellipse` clipped to a half-width `ClipToBounds` column (no
arc/donut geometry needed) with `RingGlyph` centered in its half. The
center tile paints at a higher `Panel.ZIndex` (`ControlTileViewModel.
RingZIndex`) so it visibly covers the middle of both half-disks, which is
what makes two solid half-disks read as a ring split in half. Each part
keeps its own independent click-to-select (so "Volume ▲" can still be
mapped separately from "Volume ▼"), but dragging any one part now moves
all three together: `MainViewModel.PrepareGroupDrag` builds its moving set
from `ClusterId`-mates when the tile isn't part of a checkbox
group-selection, reusing the existing `MoveControlGroup` mechanism
unchanged. The manual resize `Thumb` and the group-select `CheckBox` are
both hidden for any cluster tile — independently resizing or
group-selecting one third of a concentric cluster would desync the shared
bounding box the three parts depend on.

**Reason:** direct owner feedback after D-037: "the rotary encoder
circles... are way too big," with a specific requested redesign —
"combine circles for rotary encoders into a circle for the touch or the
center button and then a half circle on each side of it... two concentric
circles that the outside circle is split in half."

**Physically confirmed, 2026-09-21:** with a real Fire confirmed, all five
knobs render as compact clusters — a small center circle with a split ring
around it — instead of three separate large circles; clicking the
increase half, decrease half, and center circle independently selects
"Volume ▲"/"Volume ▼"/"Volume touch" respectively (confirmed via the
"Selected" label); dragging the center circle moved all three parts of the
Volume cluster together to a new position and back; checking "Lock layout"
correctly blocked the drag (reusing the existing lock mechanism with no
special-casing needed); no resize thumb or group-select checkbox appeared
on any cluster tile. Mute/Solo buttons and pads were re-checked and remain
unaffected (still their D-036/D-037 shapes).

Covered by `MainViewModelTests.cs`'s new
`Encoder_cluster_tiles_share_one_bounding_box_get_the_right_role_and_drag_together`
(a freshly-confirmed Fire's Volume increase/touch/decrease tiles get the
expected `RingRole`/shared `ClusterId`, share an identical bounding box, and
`PrepareGroupDrag`/`MoveControlGroup` move all three together without
touching `IsGroupSelected`) and the updated
`Grid_style_controllers_get_photo_shaped_tile_sizes_matching_their_real_proportions`
(a Fire encoder now measures 104×104, not 156×156).
`MidiControllerModelCatalogTests.cs`'s
`Akai_Fire_default_layout_mirrors_physical_shape_without_overlap` and
`DeviceAwareControllerRuntimeTests.cs`'s
`A_stale_saved_Fire_layout_is_reset_to_the_current_shape_not_scrambled_into_it`
were both updated for the new single-column-per-encoder stride, and the
former's overlap check now skips pairs that are cluster-mates by design.

## D-039 — Fit/Fill viewport fix and a bulk tile resize tool

**Status:** Implemented and physically confirmed 2026-09-21.

**Decision:** Two related fixes landed together:

1. **Fit/Fill.** `FitControllerLayoutToViewport()` (`MainWindow.xaml.cs`) used to zoom
   against `MainViewModel.LayoutSurfaceWidth`/`LayoutSurfaceHeight` — not the actual
   placed tiles' bounding box, but a grow-only high-water mark (`EnsureLayoutSurface`
   only ever takes `Math.Max` against whatever it already was, and
   `ControllerLayoutViewport_OnSizeChanged` grows it to match the viewport's own size on
   every resize). Once a tab's surface had ballooned to roughly viewport size, "Fit to
   view" fit that number to the viewport — a near no-op — regardless of how compact the
   actual arrangement was, leaving exactly the dead space the owner's screenshot showed.
   New `MainViewModel.GetLayoutContentBounds()` returns the tight bounding box of the
   currently placed tiles instead (`Controls.Max(t => t.X + t.TileWidth) + 10`, same for
   Y), and `FitControllerLayoutToViewport()` now measures against that. A new sibling
   `FillControllerLayoutToViewport()` (and "Fill to view" button) uses `Math.Max` of the
   two axis ratios instead of `Fit`'s `Math.Min` — zooms in until the content fully
   covers the viewport on at least one axis, standard "fit" vs. "fill" as in an
   image/video viewer, still a single uniform scale so nothing distorts. `LayoutSurfaceWidth`/
   `Height`'s own grow-only behavior was deliberately left alone — it's the scrollable
   canvas's drag headroom, a different concern from "what zoom level shows the content,"
   and changing it risked new edge cases for a problem Fit/Fill alone didn't have.
2. **Bulk resize.** New `MainViewModel.ResizeGroupSelectionAsync(width, height)` mirrors
   `ApplySelectedTileColorAsync`'s selection-resolution shape exactly: targets every
   checked (group-selected) tile, falling back to the single focused tile when nothing
   is checked, excludes any encoder cluster member (`ControlTileViewModel.
   IsEncoderClusterMember` — resizing one third of a D-038 ring cluster independently
   would desync its shared bounding box), clamps to the same bounds the per-tile resize
   `Thumb` already uses (48–600 wide, 48–400 tall), sets `TileWidth`/`TileHeight` on every
   target, then saves immediately. A new `ResizeTilesWindow` dialog (styled like
   `FireColorPickerWindow`) prefills from the first eligible target's current size and
   shows how many tiles will be affected; a new "Resize selected…" toolbar button opens
   it and applies the result on confirm.

**Reason:** the owner arranged the APC MINI's grid, clicked "Fit to view," and got a
screenshot showing the actual content squeezed into one corner with a large dead area —
"why is all that space wasted... maybe we should rethink the viewport... it would also
be helpful if the user could have control over the size and shape of each grid box,"
followed by a concrete example: "those fader grid boxes are too long and I want to make
them shorter on my own... a tool to adjust those sizes and then save it to the profile
at will."

**Physically confirmed, 2026-09-21:** with the real APC MINI confirmed and its 18 fader
tiles (very tall — D-037 sized them to their real ~378px measured height) contributing
to a since-grown `LayoutSurfaceWidth`/`Height`, "Fit to view" now zooms the whole
arrangement — pads, scene buttons, and the full-height fader tiles — snugly into the
viewport with no leftover dead space, matching the owner's compact arrangement instead
of some stale larger extent. "Fill to view" zoomed in further, filling the viewport
completely with a horizontal scrollbar appearing for the overflow, exactly as intended.
"Resize selected…" was exercised twice: once via the single-tile fallback (selected the
Master fader ▼ tile with no checkbox checked, dialog correctly prefilled 114×378, applied
114×150, the tile visibly shrank and the status bar confirmed the save) and once via the
bulk path ("Select all" checked all 99 tiles, the dialog correctly reported "This will
resize 99 squares" and prefilled from the first selected tile's own size, 156×75 —
confirming the group-selection wiring reaches the dialog correctly). The test tile was
resized back to its original 114×378 afterward so the profile was left unchanged by the
verification itself.

Covered by `MainViewModelTests.cs`'s new
`Layout_content_bounds_reflect_current_tile_positions_not_a_stale_grown_surface` (spreads
`LayoutSurfaceWidth`/`Height` far past the current tiles via `EnsureLayoutSurface`, then
confirms `GetLayoutContentBounds()` reports the tight current bounding box, not the stale
larger surface), `ResizeGroupSelectionAsync_resizes_only_selected_squares_clamped_and_saves`
(several group-selected tiles plus one left unselected; only the selected ones resize,
clamped to the shared bounds, with exactly one save), and
`ResizeGroupSelectionAsync_excludes_an_encoder_cluster_tile_even_as_the_sole_selection`
(a Fire encoder's touch tile as the sole focused selection is correctly skipped, with no
save). Fit/Fill's zoom/scroll mechanics themselves have no automated coverage, consistent
with the rest of `MainWindow.xaml.cs`'s cursor/`ScrollViewer` interaction code in this
project — verified physically instead.

## D-040 — Batch source and website publication checkpoint

**Status:** Approved by the owner 2026-09-21.

**Decision:** Synchronize the accumulated source, regression tests, development
records, and approved application artwork to the existing public Tappy repository.
Track the self-contained `WEBSITE` source and static output in the same checkpoint.
Refresh the website to describe current Fire OLED modes, LED animations,
multi-controller banks, combinations, model-specific layouts, and layout tools,
while retaining exact-model limits and the development/certification caveats.
Update the local `J:\TerkWerX page\tappy` mirror before the cPanel Tappy directory.

The owner explicitly approved the newer Targus, Microsoft, Tartarus, and Fire
photos for GitHub and website publication; the narrow asset scope is recorded in
`ASSET_INVENTORY.md`. Generated `publish/` folders, build/test output, local user
profiles, deployment archives/hosting state, raw artwork, and the internal
machine-access handoff remain local and ignored. This does not create a binary
release, change the software license, or imply completion of driver certification.

**Verification baseline:** the current Debug solution builds with zero warnings
and errors; all 925 automated tests pass; `dotnet format --verify-no-changes
--no-restore` passes. This source-tree check adds no new physical-hardware evidence.

**Website deployment:** the local mirror and cPanel Tappy directory were synced
on September 21. All 13 live files match the local payload, except for the known
hosting-provider performance-script insertion in the otherwise matching HTML.
Pre-sync local and host copies were preserved. Website checks cover its local
references, PNG dimensions, 36 demo selections, and 12 representative contrast
pairs (minimum 6.78:1); a narrow-screen hero-background overflow was corrected
during browser preview. No host-wide settings or sibling websites were changed.
