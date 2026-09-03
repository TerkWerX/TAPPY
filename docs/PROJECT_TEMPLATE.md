# Tappy living project template

> Status: project-start specification
>
> Product owner: TerkWerX
>
> Working directory: `F:\TAPPY`
>
> Reference application: `F:\TIPPY` (read-only while bootstrapping Tappy)
>
> Initial platform: Windows 11 x64
>
> Initial version: `0.1.0`

This document is the durable specification for Tappy. Update decisions and status
here as the implementation develops; do not silently weaken an acceptance rule to
make a feature appear complete.

## 1. Product definition

Tappy is Tippy's hand-operated sister application. It turns a deliberately selected
USB numpad, compact keyboard, gaming keypad, macro pad, button box, or full keyboard
into a visual, low-latency programmable controller. It keeps Tippy's approachable
TerkWerX style and deep automation system while replacing pedal-specific concepts
with devices, keys, buttons, layers, and control surfaces.

Suggested family tagline: **Put every key to work.**

### Primary users

- People repurposing a spare numpad or small keyboard as a macro controller.
- Gamers using a dedicated keypad alongside a normal keyboard/controller.
- Streamers and camera operators controlling OBS, vMix, Streamlabs, XSplit, or
  Wirecast without taking focus away from production.
- Artists, editors, CAD users, developers, medical/IT staff, and office users who
  want application-specific shortcuts under one hand.
- Musicians using keyboard-style or, later, MIDI-capable pad controllers.
- Accessibility users who benefit from larger keys, alternate layouts, layers,
  sticky actions, or one-handed workflows.

### Product promises

- Selected-device input is visible, understandable, and never collected secretly.
- Idle CPU usage stays near zero; normal input is event driven.
- Press, release, held-output, unplug, suspend, lock, and shutdown behavior is safe.
- Multiple devices and multiple simultaneous keys work correctly.
- Profiles remain local, portable, versioned JSON with explicit migrations.
- Tappy never claims to create a firmware-level keyboard or bypass application,
  Windows, administrator, anti-cheat, AppLocker, WDAC, or PowerShell restrictions.
- Hardware compatibility claims are based on real-device evidence, not appearance.

## 2. Scope and terminology

Use neutral domain names in new code:

| Tippy term | Tappy term |
|---|---|
| Pedal/device | Controller/device |
| Pedal switch | Key/control |
| Bank | Layer (the UI may say banks/layers during migration) |
| Foot combination | Chord |
| Foot sequence | Key sequence or leader sequence |
| Pedal artwork | Controller layout/artwork |
| Hardware Passport | Controller Passport |

Tappy must support at least three independent layers per device for Tippy parity,
but the data model must not hard-code the number three. Design for a configurable
layer count so future versions can safely offer more without a schema rewrite.

### Initial supported trigger sources

1. Windows Raw Input keyboard-class devices, including separate numpads and gaming
   keypads that enumerate as keyboards.
2. Consumer-control keys exposed by the same selected physical device when Windows
   reports them reliably.
3. The exact Logitech G13 `046D:C21C`, `FF00:0000` vendor-HID collection through a
   dedicated model-specific provider. This is not generic learned-HID support.

### Planned extension points

- MIDI input notes, CC, pads, knobs, and transport controls.
- Generic raw-HID digital controls through reviewed learned data definitions.
- HID rotary encoders and relative dials.
- Joystick/game-controller buttons and axes as macro triggers.
- Vendor APIs only through reviewed, bounded adapters—not arbitrary executable
  community plug-ins.

## 3. Critical Windows input truth

This section is non-negotiable.

### Device identity

Use Windows Raw Input (`WM_INPUT`) to retain the originating physical device handle.
Use `WM_INPUT_DEVICE_CHANGE`/device notifications for hot-plug. Store a privacy-safe
identity built from VID/PID, usage page/usage, report or capability fingerprint,
manufacturer/product, serial when available, and a user-confirmed alias. Raw device
paths may be used locally but must be hashed or removed from reports.

Identical devices must remain independently configurable. Prefer serial number;
otherwise use a remembered port/path fingerprint and provide a reconnect/identify
workflow when Windows changes it.

Keep separate session, model, persistent-instance, and user-profile identities.
Expose identity confidence such as `SerialExact`, `PortBound`, `Ambiguous`, or
`SessionOnly`. If two serial-less identical units swap ports, use a press-to-identify
rebind wizard instead of silently attaching the wrong mappings.

Run native input registration and parsing on one dedicated message thread/message-
only window. Publish normalized state transitions to the engine without routing the
high-rate input path through WPF. Group compatible interfaces only when Windows
supplies the same nonempty authoritative ContainerId; without that evidence, keep
them as separate choices. Do not duplicate a physical press across interfaces.

### Pass-through versus suppression

Raw Input identifies devices but does **not** safely suppress one physical
keyboard's legacy keystrokes system-wide. Therefore:

- Default to **Monitor + map** mode: Tappy runs the assignment while the original
  key may still reach the focused application.
- Display pass-through state prominently. Never imply that a key has been consumed
  when it has not.
- Offer a foreground-only capture/editor mode when Tappy owns focus.
- Encourage dedicated controllers configured to unused keys such as F13–F24 where
  practical.
- Treat true per-device exclusive/suppressed mode as a later optional subsystem
  requiring a technically valid, signed, reviewed driver or vendor-supported
  mechanism, administrator installation, recovery instructions, and explicit user
  consent. Do not fake it with timing correlation between Raw Input and a global
  low-level hook.
- Never install or enable a filter driver automatically, and never bundle HidHide,
  Interception, or a similar component without separate legal, security,
  compatibility, signing, and anti-cheat review.

Store requested source mode separately from the effective mode. Every controller
card displays **Pass-through**, **Global block**, **Exclusive**, or **Needs
attention**. If a backend fails, Tappy fails open and never pretends blocking is
active.

### Feedback-loop prevention

Tappy's own `SendInput` output must never re-trigger Tappy macros. Tag injected
events with a process-specific `dwExtraInfo` marker where supported, track active
output ownership, ignore self-generated input, and add recursion/depth/rate guards.
Test direct mappings, cyclic mappings, held keys, shared modifiers, and multiple
devices. An emergency stop must remain available even if a full keyboard is mapped.

### Keyboard semantics

Preserve scan code, extended-key flags, make/break state, left/right modifiers,
numpad versus navigation meanings, Num Lock state, media/consumer keys, repeats,
dead keys, and layout/locale. Do not reduce identity to display characters. Support
6KRO and NKRO reports where the hardware provides them, and never manufacture
simultaneous capability a device does not report.

Bindings use stable string `ControlId` values derived from provider + usage/scan-code
identity, not positional integers or a 32-bit button mask. A portable layer must map
by compatible control IDs through a preview; “enough buttons” is not sufficient when
two physical layouts label or report those buttons differently.

## 4. Required Tippy feature parity

Every item needs implementation evidence and tests before parity can be claimed.

### Assignments and outputs

- Individual key, scan-code-aware key combination, and ordered keyboard sequence.
- Text strings and literal Unicode text.
- Timed recording with balanced key-up events when recording stops.
- Mouse clicks, held buttons, movement, drag, vertical/horizontal scroll.
- Program launch with arguments and working directory.
- Multiline Windows PowerShell 5.1 or PowerShell 7 command under the current user,
  without elevation or execution-policy bypass.
- MIDI note on/off, CC, and program-change output with selectable/testable endpoint.
- OSC messages with named endpoint presets and packet test screen.
- Optional virtual Xbox buttons, sticks, and triggers with driver readiness test.
- Layer switch, next/previous layer, temporary momentary layer, and return layer.
- Variables such as `{date}`, `{time}`, `{clipboard}`, `{app}`, `{profile}`,
  `{device}`, `{control}`, `{layer}`, and named custom variables with preview.

### Per-control behavior

- Separate press and release assignments.
- Run once, hold until physical release, repeat while held, and safe toggle.
- Adjustable tap, double-tap, and long-press actions with conflict validation.
- Chords across one or more physical controllers.
- Ordered/leader sequences with timeouts.
- Explicit chord policy: additive chords may run alongside member keys; consuming
  chords replace member actions and disclose their intentional decision delay.
- One-shot modifiers and one-shot layers as a Tappy-specific enhancement.
- Reference-counted held outputs so two controls sharing a modifier do not release
  each other's state.
- Synthetic release on disconnect, profile change, Windows lock/suspend, emergency
  stop, and normal/abnormal shutdown recovery.
- Freeze the binding, layer, application scene, source-handling decision, and release
  behavior at key-down so a mid-hold context change cannot run the wrong key-up.

### Profiles and application awareness

- Independent layers and assignments per physical controller.
- Save/load/copy a portable layer to any device with sufficient controls.
- Optionally load the same layer on several devices.
- Complete application scenes selected by foreground executable and optional window
  title, resolved on input rather than continuous process polling.
- Permission-gated local scan of installed applications, Start Menu shortcuts, and
  visible running apps; no documents/history/data scanning and no upload.
- Searchable, logically categorized Windows keys/actions and application shortcut
  catalog, including productivity, creative, CAD/3D, DAWs, streaming/OBS, browsers,
  developer tools, communications, and accessibility workflows.
- Automatic backups, rollback, portable mode, import/export, and schema migrations.
- Save immutable profile snapshots atomically; quarantine corrupt files, preserve a
  recoverable last-known-good copy, and never serialize shared mutable state while
  input/UI code is changing it.

### Background operation and safety

- System-tray operation, tray-first startup option, and Start with Windows.
- Rehearsal Mode that recognizes and illuminates input but suppresses all outputs.
- Global emergency stop that cancels playback and releases keyboard, mouse, MIDI
  notes where possible, and virtual gamepad state.
- Configurable limits for macro steps, duration, repeats, nesting, and output rate.
- An optional maximum-held-output safety timeout for wireless receivers that can
  remain connected after a sleeping keypad loses its release event; never guess that
  ordinary inactivity equals a release without an explicit policy.
- Unclean-exit recovery before input listening starts.
- Optional manual/startup update check with no account or telemetry service.

### Diagnostics and support

- Live device/key diagnostics, current layer/action overlay, routing latency, key
  rollover, repeats, device reconnects, and synthetic releases.
- Tappy Doctor readiness report covering profile storage, input registration,
  controllers, emergency shortcut, outputs, startup, and application scenes.
- Controller Passport and hardware-in-the-loop evidence for make/break, repeated
  keys, rollover, reconnect, unplug while held, output cleanup, and latency.
- Privacy-safe unknown-controller and crash reports created locally for review and
  deliberately submitted to the Tappy GitHub repository by the user.
- Authenticated, checksum-pinned, data-only controller support packs with publisher
  signatures, browsing, version tracking, and user-driven updates.

## 5. Tappy-specific capabilities

### Visual controller designer

- Auto-generate a basic grid from observed keys, then let the user choose a known
  physical template or build one using rows, key widths, spacers, clusters, labels,
  knobs, and orientation.
- Ship reviewed templates for common 17/18/21/22-key numpads, small macro pads,
  common gaming-pad shapes, 40/60/75/TKL, and 104/105-key keyboards.
- Keep images/layouts data-driven in `controller_registry.json`; never show one
  brand's picture for unrelated hardware merely because the key count matches.
- Illuminate every physically pressed key, including simultaneous presses. Show the
  active assignment and layer without obscuring neighboring keys.
- Full keyboards use a zoom-to-fit canvas and optional focus groups; avoid forcing
  the application window itself to sprout scrollbars.

### Fast mapping workflows

- **Press to select:** pressing a key on the chosen controller selects its tile.
- **Learn all:** guided sweep with duplicate/missing-key detection.
- Multi-select keys and apply/copy/clear an assignment in one operation.
- Drag assignments between keys, duplicate whole layers, and compare two layers.
- Search by physical key, output action, application command, unassigned state, or
  conflict.
- Include a source-behavior test page, rollover/ghosting visualizer, and conflict
  analyzer before arming mappings.
- Import simple CSV/JSON key maps with preview and validation.
- Optional local-only usage counters/heatmap for assigned controls; never capture
  typed content, and keep this disabled by default.

### Layers optimized for hands

- Momentary, toggle, one-shot, and application-selected layers.
- Layer keys may be dedicated or dual-role (tap action, hold layer) with adjustable
  timing and clear conflict warnings.
- Lock-screen-safe and focus-change-safe release behavior.
- A small click-through overlay can show active controller, layer, held modifiers,
  and recently triggered action without stealing focus.
- Learning/capture suspends normal assignments, requires all controls released before
  and after arming, and cannot let the key used to open/close a dialog activate a
  focused WPF button or become an accidental mapping.

## 6. User interface and TerkWerX identity

- Maintain a clear visual family resemblance to Tippy: dark/light modes, restrained
  teal/blue accents, rounded panels, clear connected state, and immediate press
  illumination.
- Begin from Tippy's established tokens—Segoe UI Variable, dark surfaces around
  `#101318`, `#181D24`, and `#222934`, mint `#66E3C4`, pressed amber `#F2C14E`, and
  danger pink `#FF7A90`—then adapt them into Tappy-specific resources with matching
  accessible light-theme values.
- Use a permanent light-gray brand badge so detailed branding stays legible in both
  themes. The Tappy wordmark should use the approved whimsical blue family style.
- Recommended mascot direction: a tasteful tattooed hand/controller motif named by
  the product owner, with no face, prepared as transparent master artwork. Use a
  placeholder until artwork is explicitly approved; do not invent final branding.
- Splash screen default: ten seconds, dismissible after first presentation, with
  `Tappy`, `by TerkWerX.com`, and the current copyright year.
- About window: version, website, GitHub/support links, acknowledgments, optional
  PayPal support link, and no donation nagging.
- Normal layouts: automatic, stacked, side-by-side, tiled with configurable columns,
  and tabbed. Preserve drag ordering.
- Distinct compact mode and a still smaller controller-only/sub-compact mode with a
  reliable return control.
- Header commands wrap into additional rows as width decreases; they never silently
  disappear.
- Measure content before setting minimum size. Avoid top-level scrollbars, fit within
  the current monitor, support low-resolution displays, and make internal controller
  canvases scale rather than pushing the window off-screen.
- Remember monitor, position, maximized state, and a separate user-selected size for
  every layout/mode. Changing layout stays anchored to the current monitor.
- Meet keyboard navigation, focus indicator, screen-reader naming, DPI scaling,
  high-contrast, and light/dark text contrast requirements from the beginning.

## 7. Architecture template

Recommended solution:

```text
Tappy.slnx
src/
  Tappy.Core/             platform-neutral models, profiles, layers, gestures,
                          chords, safety ledgers, and parsers
  Tappy.Windows/          Raw Input, HID, foreground app, SendInput, MIDI/OSC,
                          gamepad, startup, and Windows-specific services
  Tappy.App/              WPF UI and composition root
tests/
  Tappy.Core.Tests/
  Tappy.Windows.Tests/
  Tappy.App.Tests/
  Tappy.G13Hil.Tests/
tools/
  Tappy.DeviceProbe/      descriptor-only schema-3 controller inventory
  Tappy.G13Hil/           explicitly armed, finite aggregate G13 input verifier
  Tappy.PackSigner/       support-pack publisher tooling
controller-packs/         schema, trust store, catalog fixtures
docs/                     architecture, privacy, protocols, testing, releases
.github/workflows/        Windows CI and tagged releases
installer/                unique Tappy Inno Setup definition
```

Use interfaces at platform boundaries: `IInputDeviceProvider`, `IOutputService`,
`IForegroundContext`, `IProfileStore`, `IClock`, `IDiagnosticsSink`, and
`IControllerRegistry`. Keep WPF types out of `Tappy.Core`.

Do not create a shared cross-repository TerkWerX package on day one. Copy only code
owned by this project from the known Tippy baseline, generalize names, preserve
tests, and record provenance. Consider a shared package later only after both apps'
boundaries are stable.

## 8. Data and identity isolation

Tappy must have its own:

- Assembly/root namespace: `Tappy` / `Tappy.*`
- Executable and process name: `Tappy.exe`
- Mutex, startup registry value, AppUserModelID, notification icon identity, and
  installer `AppId` (never reuse Tippy's GUID)
- Data root: `%LOCALAPPDATA%\Tappy`
- Portable data root and recovery marker
- Profile extensions: `.tappy.json`, `.tappy-layer.json`, `.tappy-device.json`
- Evidence: `.tappy-passport.json`, `.tappy-hil.json`, `.tappy-doctor.json`
- Support packs: `.tappy-controller-pack.zip`
- GitHub URLs: `TerkWerX/TAPPY`
- Website/update path: `https://www.terkwerx.com/tappy/`
- Unique global hotkey defaults and conflict detection so Tappy can run beside
  Tippy, which may already own its bank and emergency shortcuts.

Never read or overwrite `%LOCALAPPDATA%\Tippy` automatically. A future Tippy import
must be an explicit previewed conversion that copies data and never mutates the
source.

A future versioned `.terk-macro.json` format may share output-only macro definitions
between Tippy and Tappy, but neither application may depend on the other and device,
layer, or input bindings must not leak through that interchange.

## 9. Security and privacy requirements

- No account, cloud requirement, analytics, automatic telemetry, hidden keyboard
  inventory, or background upload.
- Listen only as broadly as Windows requires, but process/log only deliberately
  selected devices. Never log normal typed characters or text from unselected
  keyboards.
- Diagnostics default to aggregate counts, timing, rollover, and current-state data.
  A sequence of keyboard key codes can reveal typed content, so any key-code sample
  requires a short, visibly armed capture for the selected controller and is omitted
  from ordinary support exports. Hash paths and apply redaction. Exclude
  user/computer names, profiles, macro text, clipboard, arguments, PowerShell
  content, secrets, and document/window contents.
- Imported profiles, layers, and packs receive a safety preview. Program launch,
  PowerShell, network OSC, and other active content must be visibly flagged before
  imported actions are enabled.
- PowerShell runs non-interactively as the current user with cancel/timeout support;
  never request elevation or bypass execution policy.
- Support packs remain data-only. Reject executable code, traversal, symlinks,
  unexpected types, oversize entries, bad hashes, unknown signing keys, downgrade,
  and archive bombs.
- Explain SendInput/elevation/secure-desktop/game anti-cheat limitations plainly.

## 10. Performance and reliability targets

- Event-driven idle CPU near zero and no high-frequency device polling loop.
- Input receipt to output dispatch: median below 1 ms and p99 below 5 ms for verified
  ordinary mappings on supported hardware, measured honestly at software boundaries.
- UI work never blocks the input path; marshal only compact state changes.
- Bounded queues, cancellation, and rate limits prevent macro floods.
- Hot-plug without restart; reconnect uses bounded exponential backoff.
- Unplug while held releases all outputs owned by that physical control immediately.
- Multiple keyboards, rollover, shared modifiers, and concurrent macros are covered
  by deterministic tests.

## 11. Test matrix and completion gates

### Automated tests

- Profile schema round trips and migrations.
- Scan-code identity, extended keys, left/right modifiers, Num Lock variants,
  repeats, make/break, media keys, and locale-independent display formatting.
- Multi-device isolation, identical devices, hot-plug, reconnect, and device-path
  change handling.
- 6KRO/NKRO rollover, chords, sequences, layers, dual-role timing, and conflicts.
- Injected-event rejection and cycle/recursion/rate protection.
- Shared held-output ownership and cleanup on every lifecycle event.
- Macro outputs, variables, safety limits, cancellation, and dangerous-import review.
- Application scenes and permission-gated installed-app discovery.
- Light/dark/high-contrast labels and responsive layout calculations.
- Window placement, per-layout sizing, low-resolution monitors, multi-monitor/DPI,
  compact/sub-compact transitions, and no unintended scrollbars.
- Crash/support-report redaction and support-pack authentication.

### Packaged-artifact tests

- Build and test the actual published directory, portable ZIP, and installer payload,
  not only `bin/Release`.
- Verify every required registry/layout/artwork file exists where runtime file APIs
  expect it. If content is path-loaded, mark it `ExcludeFromSingleFile` or embed and
  access it through resource APIs deliberately.
- Generate a release manifest containing file paths, sizes, and SHA-256 hashes.
- Pin or record .NET SDK, dependency, and Inno Setup versions so size and content
  changes are explainable.
- Launch a clean portable copy, load its controller registry, exercise a harmless
  mapping in Rehearsal Mode, and run Tappy Doctor.

### Hardware-in-the-loop gates

- At least one standard USB numpad and one gaming/macro keypad.
- Two keyboards connected simultaneously, including identical models when possible.
- Every key make/break, held repeat, representative rollover, rapid alternating
  keys, unplug while held, reconnect, suspend/resume, and Windows lock/unlock.
- Input-to-output latency evidence and external loopback when claiming full physical
  latency.
- Promote hardware to “verified” only after stored Controller Passport and HIL
  evidence pass review.

## 12. Release and website template

- GitHub repository: the existing public `https://github.com/TerkWerX/TAPPY` is the
  owner-authorized source/documentation/CI publication destination.
- Tag-driven Windows CI: restore, test, publish self-contained win-x64, package ZIP,
  build unique per-user Inno installer, optional Authenticode signing, checksums,
  package manifest, and GitHub Release.
- Installer must use a unique AppId and `%LOCALAPPDATA%\Programs\Tappy`; never upgrade
  or uninstall Tippy.
- Release remains unsigned unless signing secrets are configured; describe
  SmartScreen honestly.
- Build into a newly created, allowlisted staging directory. Reject stale logs,
  reports, user profiles, raw device paths, secrets, or undeclared assets. Sign the
  actual discovered PE inventory and verify every signature; never assume a DLL
  exists because an older non-single-file build produced it.
- Keep website source/build trees outside hosting synchronization directories.
  Upload only a reviewed static `upload-ready` package.
- Tappy page should cross-link to Tippy and the TerkWerX home page; Tippy may tease or
  link Tappy only when the owner requests publication.
- Website download code should query the latest GitHub `Setup-x64.exe` release and
  retain one verified local fallback with matching SHA-256 metadata.
- Generate website release metadata from the release artifact as one versioned source
  of truth; do not maintain drifting `source`, `deploy`, and hosted version records by
  hand.
- Source/documentation/CI publication to the named repository is authorized.
  Packaged software releases, signing, website publication, production hosting, and
  final branding still require the product owner's explicit instruction at that
  stage.

## 13. Phased implementation plan

### Phase 0 — Foundations

- Record decisions, parity matrix, architecture, privacy model, and input limitations.
- Create the solution/projects/tests with unique Tappy identity and no Tippy writes.
- Port/generalize macro models, output services, held-output safety, profiles, app
  scenes, catalogs, and theme primitives behind interfaces.

### Phase 1 — Safe vertical slice

- Enumerate Raw Input keyboards without logging their typing.
- User deliberately selects and identifies a spare numpad.
- Generate a visual layout, illuminate press/release and simultaneous keys.
- Map one selected key to one harmless output in Rehearsal Mode and normal mode.
- Ignore self-injected input, release held output on unplug, save/reload the profile.
- Display an unavoidable, accurate pass-through notice.

### Phase 2 — Tippy parity

- Full outputs, layers, gestures, chords/sequences, application scenes, profiles,
  compact UI, tray/startup, diagnostics, recovery, update checks, and catalogs.

### Phase 3 — Tappy differentiators

- Layout designer, batch mapping, dual-role/one-shot layers, overlay, support packs,
  Controller Passport, HIL tools, and additional trigger providers.

### Phase 4 — Release readiness

- Accessibility/contrast/DPI audit, performance evidence, packaged-artifact audit,
  real-device verification, documentation, installer, GitHub release, and website.

## 14. Open decisions log

Record owner-approved answers here rather than burying them in code:

| Decision | Status | Resolution |
|---|---|---|
| Final mascot/wordmark artwork | Open | Use placeholders until approved |
| Public license | Open | Source may be public, but all rights remain reserved; define license and contribution terms before accepting external contributions or releasing binaries |
| Initial verified controller models | Open | Based on available real hardware |
| Layer limit exposed in v0.1 | Open | Model remains configurable |
| Optional exclusive/suppressed input | Deferred | Requires valid driver strategy and review |
| MIDI input in first public release | Open | Architecture supports it; prioritize after Raw Input slice |
| Windows 10 support | Out of initial scope | Windows 11 x64 is the tested baseline |
| Future macOS port | Architectural consideration | Keep core portable; no unverified promise |

## 15. Definition of done for the first milestone

The first milestone is complete only when a clean checkout can build and test; a user
can deliberately select one spare numpad; its distinct physical key make/break and
simultaneous presses illuminate correctly; one mapping works without recursively
triggering itself; unplugging a held key releases output; the saved profile reloads;
Rehearsal Mode suppresses output; pass-through behavior is stated accurately; Tappy
uses no Tippy data/identity; and automated tests plus a concise manual test record
support every claim.
