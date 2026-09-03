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

## D-010 — Unique application/release identity

**Status:** Accepted.

**Decision:** Use executable `Tappy.exe`, root namespace `Tappy.*`, data root
`%LOCALAPPDATA%\Tappy`, AppUserModelID `TerkWerX.Tappy`, mutex
`Local\TerkWerX.Tappy.HandController.0_1`, startup value `Tappy`, emergency hotkey
`Ctrl+Alt+Shift+F12`, a newly generated installer AppId, Tappy extensions, and only
Tappy endpoints. The reserved update endpoint is the Tappy-only GitHub
`releases/latest` API URL; no update check is implemented or enabled in this
milestone. A code-rendered `T` tray glyph is a placeholder, not final artwork.

## D-011 — Source publication is authorized; release decisions remain open

**Status:** Partially resolved 2026-09-02.

The owner authorized publishing Tappy source, documentation, and CI configuration to
the public `https://github.com/TerkWerX/TAPPY` repository. Final mascot/wordmark,
artwork licensing, public software license, verified device list, signing
certificate, driver-based exclusivity, packaged software release, website
publication, and production hosting remain open or deferred and are not implied by
source-publication authorization. Until license and contribution terms are selected,
all rights are reserved and external contributions must not be submitted or merged.

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

**Status:** Accepted; physical HIL pending.

**Decision:** The dedicated G13 provider accepts only the physical `046D:C21C`,
`RIM_TYPEHID`, `FF00:0000` collection, validates its fixed eight-byte input report,
and exposes 39 code-defined controls. `046D:C232` is the G HUB virtual keyboard and
is never G13 identity. Tappy sends no G13 output reports and does not claim generic
Logitech, learned-HID, LCD, lighting, or memory-mode support.

**Reason:** Exact matching and strict decoding keep a model-specific protocol from
becoming a misleading generic-HID claim. Deterministic tests support the code; the
attached device remains below Functional/Verified until a physical run succeeds.

## D-014 — External implementation provenance stays clean-room

**Status:** Accepted.

**Decision:** Public platform and protocol documentation may inform an independent
Tappy implementation. An owner-authorized internal donor audit found no reusable
G13 or SpacePilot implementation; no donor code, repository history, or logs were
copied. Tappy's G13 support was implemented clean-room from public platform/protocol
documentation and project-owned design.

**Reason:** This preserves an auditable public-source boundary without implying
protocol compatibility between the G13 and SpacePilot Pro.
