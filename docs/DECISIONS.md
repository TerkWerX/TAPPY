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
