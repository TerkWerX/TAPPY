# Codex kickoff prompt — Tappy

You are starting a new TerkWerX project named **Tappy** in `F:\TAPPY`.

Tappy is the hand-operated sister application to Tippy. Where Tippy turns USB foot
pedals into programmable controls, Tappy turns a deliberately selected USB numpad,
compact keyboard, gaming keypad, macro pad, button box, or—when safely and
explicitly chosen—an entire keyboard into a visual, low-latency programmable control
surface. Tappy should feel unmistakably related to Tippy and eventually provide the
same depth of programmability, but its architecture and vocabulary must be native to
keyboards and hand controllers rather than a pedal application with labels changed.

Work locally and persistently until the first milestone below is genuinely complete.
Do not publish, push, create a GitHub repository, create a public release, sync a
website, or change production hosting unless I explicitly ask at that later stage.

## Read before changing anything

1. Read `F:\TAPPY\START_HERE.md`,
   `F:\TAPPY\docs\PROJECT_TEMPLATE.md`, and
   `F:\TAPPY\docs\TIPPY_REUSE_MAP.md`, and
   `F:\TAPPY\docs\ASSET_INVENTORY.md` completely. They are authoritative for this
   bootstrap unless I override them.
2. Treat `F:\TIPPY` as a read-only reference. Read its current `README.md`,
   `CHANGELOG.md`, `docs/ADVANCED_INTERACTIONS.md`, `docs/COMPATIBILITY.md`,
   `docs/HARDWARE_TEST_STATION.md`, `docs/PEDAL_SUPPORT_PACKS.md`, and
   `docs/RELEASING.md`, plus the relevant core models, services, tests, project files,
   installer, and GitHub workflows.
3. Run Tippy's existing tests only if useful for establishing the reference baseline.
   Do not edit, reformat, version, commit, release, or move Tippy files.
4. Inventory any existing files in `F:\TAPPY` before scaffolding. Preserve this
   prompt, the project template, and every original under `PAD IMAGES`.

## Product objective

Build Tappy as a Windows 11 x64, .NET 8, WPF utility with event-driven input and
near-zero idle work. Its first safe input provider is Windows Raw Input for
keyboard-class devices, retaining the originating physical device. The architecture
must also permit learned raw-HID controls and future MIDI, encoder, and joystick
trigger providers without forcing those concepts into the core profile model.

Run native input registration and parsing on one dedicated message thread/message-
only window. Normalize events and route them to the engine independently of WPF;
throttle only visual updates. Confidently group keyboard, consumer-control, and
vendor-specific collections belonging to one composite device without duplicating
events.

Use neutral Tappy terms in new code: controller/device, key/control, layer, chord,
sequence, and controller layout. Support at least three independent layers per
device for Tippy parity, but do not hard-code three into the schema or engine.

Suggested product line: **Put every key to work.** Treat that as provisional copy,
not permission to finalize branding without review.

## The critical keyboard-input constraint

Do not gloss over this or pretend Windows can do something it cannot.

Windows Raw Input can distinguish the source physical keyboard through the raw input
device handle. It is observational: it does not safely prevent one chosen keyboard's
ordinary keystrokes from reaching every other foreground application.
`RIDEV_NOLEGACY` affects legacy messages to the registering application, not a
system-wide per-device block. A low-level keyboard hook can block globally but does
not carry a reliable physical device handle. Do not correlate hook and Raw Input
events by timestamp to claim selective suppression; that design is race-prone.

For the initial implementation:

- Default to an accurately labeled **Device-aware pass-through** mode. Tappy runs
  the assignment, but the original key may also reach the focused application.
- Process only a controller the user deliberately identifies and selects. Never
  silently select the first keyboard and never record normal typing from unselected
  keyboards.
- Encourage a spare numpad/controller configured to harmless or unused keys such as
  F13–F24 where practical.
- A foreground-only editor capture mode is allowed while Tappy owns focus.
- If a global blocking mode is ever considered, label clearly that it affects every
  keyboard event matching the rule and protect an escape path. It is not the default
  or part of the first milestone.
- True per-device exclusive/suppressed input is a later, separately approved design
  requiring a valid signed fail-open driver or vendor-supported mechanism, explicit
  administrator installation, recovery instructions, legal/security review, and
  anti-cheat compatibility review. Never install such a component silently.
- Until that exists, do not claim that a secondary full keyboard has been completely
  remapped independently of its original input.

Store the requested source mode separately from the effective source mode. Every
controller shows **Pass-through**, **Global block**, **Exclusive**, or **Needs
attention**. If a backend is unavailable or fails, Tappy fails open and never
pretends that blocking is active.

Prevent output feedback loops from day one. Tag Tappy's own `SendInput` events with
a process-specific marker where supported, ignore self-generated input, track
execution ancestry, and enforce recursion/depth/rate limits. Test cycles such as
`A→B`, `B→A`, shared modifiers, held mappings, two controllers, and hardware
autorepeat. Tappy must never macro itself into an input storm.

Model physical identity using scan code, E0/E1/extended state, make/break, HID usage
when available, and the originating device—not only a virtual key or character.
Distinguish numpad keys from navigation/top-row keys, left/right modifiers, numpad
Enter, OEM keys, media/consumer keys, lock-state effects, dead keys, and locale.
Treat OS autorepeat separately from physical taps. Preserve real 6KRO/NKRO state and
never invent simultaneous capability the device does not report.

Use stable string `ControlId` values based on provider + physical usage/scan-code
identity, not positional indexes or a 32-bit mask. Tappy must scale beyond 100 keys.
Portable layers map through compatible control IDs and a preview; never silently
assign “key 7” merely because a destination has enough keys.

## Required Tippy parity baseline

Create and maintain `docs/PARITY_MATRIX.md` mapping every capability to Tappy status,
source component, adaptation, tests, and remaining hardware evidence. At minimum the
eventual product must include:

- Multiple simultaneous physical controllers with independent identities, layouts,
  layers, mappings, ordering, and hot-plug behavior.
- Portable layer save/load/copy, including using the same layer on compatible
  devices and explicit validation when control counts differ.
- Keyboard keys/combinations/sequences, Unicode text, timed recording, delays,
  mouse clicks/movement/drag/vertical and horizontal scroll, program launch,
  multiline PowerShell 5.1/7, MIDI output, OSC output, optional virtual Xbox buttons,
  sticks and triggers, variables, and layer-control steps.
- Separate press and release actions; run once, hold until release, repeat, toggle,
  tap, double tap, long press, and validated conflicts.
- Momentary, toggle, and one-shot layers; dual-role keys; chords across devices; and
  ordered/leader sequences.
- Explicit chord policy: additive chords may run with their member actions; consuming
  chords replace member actions and make their intentional decision delay visible.
- Reference-counted held output shared safely between simultaneous controls, with
  cleanup on physical release, unplug, profile/layer change, Windows lock/suspend,
  emergency stop, crash recovery, and exit.
- Freeze the binding, layer, application scene, source-handling decision, and release
  behavior at key-down so a context change during a hold cannot run the wrong key-up.
- Complete foreground application scenes by executable and optional window title,
  resolved on input rather than through continuous polling.
- Permission-gated local discovery of installed applications, Start Menu shortcuts,
  and visible processes, followed by a user review list. Never scan documents,
  browser history, application data, or upload an inventory.
- Logically organized and searchable Windows-key/action and application shortcut
  catalogs, including Office, browsers, Adobe/creative tools, GIMP, Blender, Maya,
  CAD, DAWs, OBS/streaming/video switching, communications, accessibility, and
  developer tools.
- Reusable MIDI selection/test, reusable OSC endpoint presets/test, and a friendly
  named-variable manager with previews.
- Rehearsal Mode, global emergency stop, bounded macro duration/steps/repeat/output
  rate, and release-all-held-inputs command.
- Tray/background operation, start with Windows, tray-first option, light/dark mode,
  automatic backups, rollback, portable mode, unclean-exit recovery, and optional
  update checks without telemetry.
- Immutable profile snapshots, atomic replacement, corrupt-file quarantine, tested
  migrations, and a recoverable last-known-good copy; never serialize shared mutable
  state while UI/input code is changing it.
- Live diagnostics, click-through active-layer/action overlay, Tappy Doctor,
  privacy-safe crash/unknown-controller reports, Controller Passport, HIL test
  station, and authenticated checksum-pinned data-only controller support packs.

Reuse Tippy's tested concepts where appropriate, but copy them into Tappy and
generalize them deliberately. Do not create a fragile shared cross-repository package
yet. Keep WPF and Windows-native types out of the platform-neutral core.

## Tappy-specific experience

Create a data-driven controller layout system rather than three oversized pedal
tiles:

- Generate a simple key grid from learned/observed controls.
- Support reviewed templates for common numpads, macro pads, gaming keypads,
  40/60/75/TKL, and 104/105-key keyboards.
- Let users construct layouts from rows, key widths, gaps, clusters, orientation,
  labels, and future knobs/encoders without stretching or falsifying product art.
- Pressing a selected controller key should select and illuminate its on-screen key.
  Simultaneous keys illuminate simultaneously.
- Display assigned action and active layer clearly without obscuring neighboring
  keys.
- Allow multi-select/batch assignment, drag/copy mappings, duplicate/compare layers,
  press-to-select, guided learn-all, and search for physical keys, output actions,
  application commands, conflicts, or unassigned controls.
- Include a source-behavior test page, rollover/ghosting visualizer, and conflict
  analyzer before mappings are armed.
- Use zoom-to-fit and optional keyboard groups for full-size layouts. Do not make the
  main application window enormous or introduce top-level scrollbars.
- Add local-only optional assignment usage counts/heatmaps later, disabled by
  default and recording control IDs only—never typed content.

Plan trigger-provider interfaces for MIDI input pads/notes/CC, HID encoders, and
joystick buttons/axes. Do not claim those providers before they are implemented and
tested with real hardware.

Learning/capture must suspend normal assignments, require all controls released
before and after arming, and prevent the key used to open or close a dialog from
activating a focused WPF button or becoming an accidental mapping.

## UI and brand requirements

Tappy must look like a deliberate TerkWerX sister product:

- Follow Tippy's dark/light family, teal/blue accents, rounded panels, clear status,
  immediate press illumination, and simple utility-first presentation.
- Begin from the family tokens in Tippy—Segoe UI Variable, dark surfaces around
  `#101318`, `#181D24`, and `#222934`, mint `#66E3C4`, pressed amber `#F2C14E`, and
  danger pink `#FF7A90`—with accessible matching light-theme resources.
- Give detailed branding a permanent light-gray badge so it stays legible in either
  theme. Use an approved whimsical blue Tappy wordmark when it exists.
- A tattooed hand/controller mascot is a recommended direction, with no face, but
  use placeholders until I approve final artwork. Do not silently reuse the Tippy
  foot as Tappy's permanent mascot or generate final brand assets without review.
- Use a ten-second branded splash screen, with a sensible dismissal behavior,
  `Tappy`, `by TerkWerX.com`, version, and current copyright year.
- Include a normal About window, TerkWerX/Tappy/GitHub/support links, acknowledgments,
  and the same optional PayPal support link without donation nags.
- Provide auto, stacked, side-by-side, tiled/column, and tabbed controller layouts,
  plus distinct compact and controller-only/sub-compact modes with a reliable return
  control.
- Header commands wrap as width shrinks and never disappear.
- Remember monitor, position, maximized state, controller order, selected tab, and a
  different user-set size for every layout/mode. Layout changes stay anchored to the
  current monitor.
- Fit low-resolution displays, avoid unintended window scrollbars, and handle
  per-monitor DPI changes. A keyboard canvas may pan/zoom internally without making
  the whole application awkward.
- Build keyboard navigation, automation/screen-reader names, focus visuals,
  high-contrast behavior, and tested light/dark text contrast from the beginning.

## Safety, privacy, and honest limitations

- No account, cloud dependency, analytics, automatic telemetry, hidden keyboard
  inventory, or background uploads.
- Never log characters or ordinary typed text. A chronological key-code list can
  also reveal what was typed, so default diagnostics retain only aggregate selected-
  device counts, timing, rollover, and current state. Any key-code sample requires a
  short visibly armed capture and is omitted from ordinary support exports.
- Hash or redact raw device paths. Reports exclude user/computer names, profile and
  macro bodies, text assignments, clipboard, arguments, PowerShell, secrets,
  documents, and window contents.
- Imported profiles/layers/packs must be previewed. Flag and keep disabled until
  reviewed any imported program launch, PowerShell, network OSC, or other active
  content.
- PowerShell runs non-interactively under the current account with timeout/cancel;
  it never elevates, bypasses execution policy, or weakens AppLocker, WDAC, or
  Constrained Language Mode.
- Support packs are data-only and signed. Reject code, traversal, symlinks, unknown
  types, oversized entries, archive bombs, bad hashes, unknown publishers, and
  downgrades.
- State clearly that SendInput is not firmware-level USB HID, can be rejected by
  elevated/secure/exclusive applications, and can violate game/anti-cheat rules when
  used for automation. The application's rules always win.
- Protect the user's primary keyboard. The emergency stop must remain reachable even
  when mapping a full keyboard; offer a mouse/tray escape and require a deliberate
  hold/chord on an unaffected input.

## Architecture and identity

Create this general shape unless inspection reveals a better justified boundary:

```text
Tappy.slnx
src/Tappy.Core/       platform-neutral profiles, controls, layers, gestures,
                      chords, execution models, safety ledgers, parsers
src/Tappy.Windows/    Raw Input/HID, Windows output, foreground context, MIDI/OSC,
                      gamepad, startup and Windows lifecycle
src/Tappy.App/        WPF UI and composition root
tests/Tappy.Core.Tests/
tests/Tappy.Windows.Tests/
tests/Tappy.App.Tests/
tools/Tappy.DeviceProbe/
tools/Tappy.PackSigner/
controller-packs/
docs/
installer/
.github/workflows/
```

Use interfaces at platform boundaries and inject the clock, input providers, output
services, foreground context, stores, registry, and diagnostics so core behavior is
deterministically testable.

Tappy must have a completely unique executable, assembly/root namespace, installer
AppId, application mutex, AppUserModelID, startup registry value, notification icon,
data directory, recovery marker, profile extensions, URLs, icons, issue templates,
and update endpoint. Use `%LOCALAPPDATA%\Tappy`, `.tappy.json`,
`.tappy-layer.json`, `.tappy-device.json`, `.tappy-passport.json`,
`.tappy-hil.json`, `.tappy-doctor.json`, and `.tappy-controller-pack.zip`. Never read
or overwrite `%LOCALAPPDATA%\Tippy` automatically. Any future Tippy import is an
explicit previewed copy/conversion.

Use unique global hotkey defaults and detect conflicts so Tippy and Tappy can run at
the same time. A future versioned `.terk-macro.json` may exchange output-only macro
definitions between the sisters, but neither program may depend on the other or
share device/layer/input bindings implicitly.

Do not carry over Infinity/AltoEdge decoders, three-switch masks, left/center/right
geometry, pedal filenames, Tippy GUIDs, Tippy URLs, or Tippy branding. Search the
new tree for accidental `Tippy`, `pedal`, `.tippy`, old GUID, and old URL references;
allow only intentional acknowledgments/migration documentation.

Do not copy Tippy's raw-keyboard implementation verbatim: it stores only a virtual
key, uses path-centric identity, caps controls at 32, registers only the keyboard
usage, routes events through the WPF dispatcher, does not suppress source input, and
does not tag `SendInput.ExtraInfo`. Those are explicit Tappy redesign points.

## Performance and reliability targets

- Event-driven idle CPU near zero; no high-frequency keyboard polling loop.
- Input receipt to output dispatch median under 1 ms and p99 under 5 ms for a simple
  verified mapping, measured at honest software boundaries.
- UI rendering never blocks input processing.
- Bounded queues, cancellation, macro rate limits, and reconnect backoff.
- Correct multiple-device concurrency, hardware repeat handling, 6KRO/NKRO state,
  and identical-device behavior.
- Immediate owned-output release on disconnect, lock, suspend, profile switch,
  emergency stop, and shutdown.
- An optional maximum-held-output timeout for wireless receivers that remain present
  after a sleeping device loses a release, without treating ordinary inactivity as a
  release by default.

## Packaging and release hygiene

Start at version `0.1.0`. Use a unique Inno Setup AppId and install per-user to
`%LOCALAPPDATA%\Programs\Tappy`. A future tag workflow should test, publish a
self-contained win-x64 app, create portable ZIP and installer, optionally sign when
secrets exist, emit SHA-256 checksums and a package manifest, and publish only after
explicit authorization.

Test the actual published directory and archive. Verify every layout/registry/artwork
file exists where runtime code expects it. If an asset is loaded by file path, mark
it `ExcludeFromSingleFile` and ship it beside the app, or deliberately embed it and
use resource APIs. Do not assume a successful compile proves the installer is
complete. Pin or record SDK, dependency, and Inno Setup versions so size changes are
explainable.

Build releases in a new allowlisted staging directory. Reject stale logs, reports,
profiles, raw paths, secrets, and undeclared files. Discover and sign the actual PE
inventory, then verify signatures; never assume a DLL exists because an older build
produced it. Smoke-test Tippy and Tappy installed and running together without shared
mutexes, data, startup entries, shortcuts, uninstall identity, hotkeys, or updates.

Keep raw source art, build caches, `bin`, `obj`, packages, local reports, installers,
website workspaces, and host-sync folders out of Git. Never put the full Tappy source
tree inside `J:\TerkWerX page` or another directory that will be uploaded wholesale.
When a website is later requested, build a reviewed minimal `upload-ready` artifact
and upload only that content. Generate release/download metadata from one versioned
release artifact rather than maintaining drifting source, deploy, and hosted copies
by hand.

## Required documents during bootstrap

Create and maintain:

- `README.md` with honest early-development status.
- `CHANGELOG.md` with `Unreleased`.
- `docs/ARCHITECTURE.md`.
- `docs/DECISIONS.md`, including pass-through/suppression and identity decisions.
- `docs/PARITY_MATRIX.md`.
- `docs/PRIVACY_AND_SECURITY.md`.
- `docs/COMPATIBILITY.md` with evidence-based support tiers.
- `docs/HARDWARE_TEST_STATION.md`.
- `docs/RELEASING.md`.
- A strong `.gitignore` before generating builds or importing artwork.

## Execution sequence

1. Inspect both project trees and summarize the inherited capabilities, reusable
   boundaries, Tappy-specific risks, and any conflict with this prompt.
2. Write the architecture, decision record, privacy model, and parity matrix before
   copying implementation code.
3. Scaffold the solution/projects/tests with unique Tappy identity and initialize a
   local Git repository if one does not exist. Do not add a remote or push.
4. Port/generalize the smallest stable Tippy primitives first: macros, outputs,
   held-state safety, profiles, variables, catalogs, scenes, and layout helpers.
   Preserve or improve their tests.
5. Implement one safe vertical slice using device-aware pass-through Raw Input:
   enumerate keyboards; require deliberate selection/identification of a spare
   numpad; display make/break and simultaneous presses; map one key through
   Rehearsal Mode and normal output; reject self-injected input; synthesize release
   on unplug; save/reload the profile; show pass-through truth prominently.
6. Add deterministic tests for scan codes, E0/E1, left/right modifiers, numpad
   distinctions, repeats, simultaneous state, multiple devices, injected loops,
   held cleanup, and profile isolation.
7. Run build/tests and a clean published-artifact smoke audit. Do not claim physical
   hardware support unless the relevant device was actually connected and tested.
8. Report what is complete, exact test results, files created, known limitations,
   and the next recommended milestone. Lead with outcomes, not activity.

Make informed, reversible local decisions rather than repeatedly stopping for minor
preferences. Stop and ask only when a missing decision would materially change
product behavior, security, hardware requirements, branding, licensing, or public
state.

## First-milestone definition of done

Do not mark the initial milestone complete until all of the following are true:

- A clean checkout builds and all automated tests pass.
- Tappy uses no Tippy executable identity, data path, installer identity, profile
  extension, mutex, startup entry, URLs, or final branding assets.
- A user must deliberately select and confirm one spare Raw Input keyboard/numpad.
- Physical key identity includes scan-code/extended/make-break information rather
  than character-only mapping.
- Press, release, repeat, and simultaneous keys illuminate accurately.
- One harmless mapping works in Rehearsal Mode and normal mode without recursively
  triggering itself.
- Original-input pass-through is labeled accurately and tested.
- Unplugging while a mapped key is held releases all output owned by that key.
- The saved Tappy profile reloads with controller identity, layout, layer, and
  mapping intact.
- Emergency stop and a mouse/tray recovery path remain available.
- Support/debug output contains no typed content or raw identifying device path.
- The actual published portable artifact starts, loads its required data, and passes
  a harmless readiness smoke check.
- `README`, architecture, decisions, parity, privacy, compatibility, and test notes
  reflect reality and do not advertise unfinished features.

Begin by reading the specified files and then carry out the execution sequence.
