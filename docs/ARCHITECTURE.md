# Tappy architecture

> Status: first-milestone contract for version 0.1.0
>
> Platform: Windows 11 x64, .NET 8, WPF
>
> Reference baseline: Tippy v0.7.0 (`642a4ff`), inspected read-only

## Outcomes and boundaries

Tappy's domain is physical controllers, stable controls, configurable layers, input
gestures, and output actions. The core does not reference WPF, Win32 handles, registry
types, or Windows key classes. Platform services implement narrow interfaces and the
WPF application is only a composition and presentation layer.

```text
Raw Input message-only window (dedicated native thread)
    -> keyboard packets OR exact Logitech G13 vendor-HID reports
    -> authoritative ContainerId grouping + sanitized device descriptor
    -> provider-native physical control signal
    -> selected-controller/identification gate
    -> state tracker (press, release, repeat, simultaneous set)
    -> mapping engine (frozen key-down execution context + safety guards)
       -> Rehearsal: record aggregate outcome only
       -> Normal: IKeyboardOutput -> tagged Windows SendInput
    -> bounded ordered WPF visual drain (presentation work never blocks routing)
```

Hot-unplug, session lock, suspend, profile replacement, emergency stop, and shutdown
all converge on the same owner-release path. Failure is fail-open with respect to the
physical keyboard: Tappy stops output and releases owned state; it never claims that
the original key was blocked.

## Projects

### `Tappy.Core`

Contains immutable/snapshot-oriented models and deterministic engines:

- `ControlId` derives from provider and physical scan/usage identity; it is a stable
  string and is never an array index or bit mask.
- `ControlSignal` carries controller session, provider-native `ControlId`,
  press/release/repeat kind, injection metadata, and a monotonic timestamp. Keyboard
  `ControlId` values encode scan code and E0/E1; G13 values encode report/button or
  thresholded stick-direction identity.
- `ControllerInputStateTracker` keeps independent sets for every session and derives a
  repeat only when a second make arrives while the same control is down.
- `ControllerActivationGate` requires an explicit target, a clean released state, a
  make/break identification sample, and a separate confirmation. No "first device"
  fallback exists.
- `MappingEngine` accepts only the confirmed persistent controller, rejects
  self-injected/device-less signals, freezes binding/layer/source/release data at
  key-down, reference-counts held output, and applies depth/rate limits.
- Profile and controller layout schemas support arbitrary control counts and an
  arbitrary positive number of layers; three starter layers are a UI default only.
- Platform interfaces include input provider, output, clock, profile store,
  foreground context, diagnostics, and controller registry seams.

### `Tappy.Windows`

- `RawInputKeyboardProvider` owns a dedicated native/background message thread and a
  message-only window. It registers Generic Desktop/Keyboard with
  `RIDEV_INPUTSINK | RIDEV_DEVNOTIFY`, enumerates keyboard handles, parses
  `WM_INPUT`, and emits hot-plug removal.
- Raw device paths exist only inside this process boundary. A SHA-256-based
  persistent instance fingerprint and a short display fingerprint are exposed;
  profiles and diagnostics never receive the path.
- `KeyboardPacketNormalizer` retains scan code, E0/E1, make/break, virtual-key
  display metadata, and `ExtraInformation`. Scan identity distinguishes top-row and
  numpad keys, navigation versus numpad variants, left/right modifiers, and numpad
  Enter where Raw Input reports the documented distinctions.
- Physical keyboard interfaces are merged into one logical controller only when
  Windows supplies the same authoritative
  [`DEVPKEY_Device_ContainerId`](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid).
  The attached K15 is one `1A2C:2D43` group with four interfaces; matching VID/PID
  without ContainerId evidence never causes a merge.
- `LogitechG13InputProvider` handles only the physical `046D:C21C`, `FF00:0000`
  vendor-HID collection. It validates the eight-byte report, exposes 39 code-defined
  controls, and excludes the `046D:C232` G HUB virtual keyboard. See the
  [G13 support boundary](LOGITECH_G13.md).
- `SendInputKeyboardOutput` tags every `SendInput` record with a process-specific
  Tappy marker and balances every owned press with a release.
- `AtomicProfileStore` snapshots before serialization, writes a temporary file in
  the destination directory, atomically replaces where supported, retains a
  last-known-good copy, and quarantines corrupt input.
- Windows lifecycle and hotkey adapters call the engine's single cleanup API.

### `Tappy.App`

The WPF shell explicitly composes the keyboard and G13 providers, displays source
truth prominently, lets the user choose/identify/confirm one device, renders a
data-driven key grid, drains visual transitions in order, provides
mapping and Rehearsal controls, and preserves mouse-accessible emergency recovery.
The visual buffer preserves press/release ordering, compacts only after a bounded
backlog, and keeps physical `IsPressed` state separate from a minimum-duration
illumination pulse so a quick tap is visible without delaying input or output.
During identification it marks WPF key events handled so the candidate press cannot
activate Tappy's own focused controls; this is local UI protection, not system-wide
suppression of the original key.
The UI never mutates the live profile while it is being serialized: it publishes a
new immutable snapshot to the engine and store.

## Physical and persistent identity

Identity has four deliberately separate scopes:

| Scope | Purpose | Lifetime |
|---|---|---|
| Session handle | Route one live `WM_INPUT` source | Until device removal/restart |
| Model identity | VID/PID, usage/capability fingerprint | Across units of a model |
| Instance identity | serial when available; otherwise hashed port/path fingerprint | Reconnect where Windows is stable |
| Profile identity | user-owned controller record and alias | Until user removes/rebinds it |

Confidence is recorded as `SerialExact`, `PortBound`, `Ambiguous`, or `SessionOnly`.
When identical serial-less devices cannot be distinguished, Tappy requires the user
to identify/rebind by pressing the intended device rather than guessing.

Composite collections are grouped only on the same authoritative Windows
ContainerId plus compatible provider identity. Without that evidence, interfaces
remain separate. A provider-scoped deduplication key prevents one report from being
published twice; visual similarity or VID/PID alone never justifies a merge.

## Concurrency and performance

Normal input is message/event driven. There is no keyboard polling loop and no
foreground-application polling loop. Native parsing and the small core route do not
enter the WPF dispatcher. Presentation transitions retain FIFO order behind one
pending dispatcher operation and a bounded buffer; overflow compaction keeps the
latest physical state plus a press pulse for each affected control. Macro work is
cancellable and bounded by step, duration,
nesting, repeat, and output-rate limits. If any future bounded queue overflows,
Tappy enters Needs attention, cancels output, and releases all owned state rather
than dropping a release invisibly.

The software timestamp boundary is Raw Input receipt immediately after parsing to
output dispatch immediately after `SendInput` returns. Median-under-1-ms and
p99-under-5-ms are targets, not claims until physical evidence is captured.

## Extension providers

`IInputDeviceProvider` is a real Core seam for discrete press/release/repeat signals
with provider-native `ControlId` values. The application now explicitly composes
`RawInputKeyboardProvider` and the model-specific `LogitechG13InputProvider`; that is
not yet a general plug-in or cross-provider composite-identity system. The G13
provider publishes raw X/Y through a provider-specific event and maps conservative
thresholds to four discrete direction controls. Generic analog profile values,
deadzones, and user-configurable thresholds remain future schema work. Generic
learned raw-HID buttons, MIDI, encoders, and joystick providers still require their
own identity, selection, UI, and test integration. Support packs remain data-only and
never load code.

## Reuse provenance

Tippy concepts reused deliberately are reference-counted held ownership, immutable
profile serialization intent, event-driven input, foreground resolution on input,
layout fit helpers, and fail-safe lifecycle cleanup. Tippy's path-only Raw Input
identity, virtual-key-only event, WPF-window registration, three-switch masks,
pedal decoders, product identities, and untagged `SendInput` implementation are not
copied.

An owner-authorized internal donor audit found no reusable G13 or SpacePilot
implementation. Tappy copied no donor code, repository history, or logs; the G13
provider was independently implemented from public platform/protocol sources and
project-owned design. See [`LOGITECH_G13.md`](LOGITECH_G13.md).
