# Tippy-to-Tappy reuse map

This map uses Tippy `v0.7.0` (`F:\TIPPY`, commit `642a4ff`) as the known reference
baseline. Tippy remains read-only while Tappy is bootstrapped. Reuse means copying
project-owned concepts and code into the new repository with deliberate renaming,
tests, and Tappy-specific identity—not linking the two working directories or doing
a blind global text replacement.

## Reuse with light generalization

| Tippy reference | Tappy destination/responsibility | Required change |
|---|---|---|
| `src/Tippy.Core/Models/MacroDefinition.cs` | `Tappy.Core` macro model | Rename context terms; preserve versioned serialization |
| `src/Tippy.Core/Models/MacroStep.cs` | Output-step model | Retain keyboard, text, mouse, program, PowerShell, MIDI, OSC, gamepad, and variables |
| `src/Tippy.Core/Input/HeldOutputLedger.cs` | Held-output ownership | Generalize owner from pedal switch to device/control; retain reference counts |
| `src/Tippy.App/Services/MacroPlayer.cs` | Macro execution coordinator | Split platform outputs behind interfaces; add recursion/rate guards and injected-input marker |
| `src/Tippy.App/Services/WindowsInputService.cs` | Windows keyboard/mouse output | Preserve balanced make/break cleanup; make scan-code semantics explicit |
| `src/Tippy.App/Services/GamepadAnalogLedger.cs` | Analog gamepad ownership | Rename only; preserve layered restore behavior |
| `src/Tippy.App/Services/VirtualGamepadService.cs` | Optional Xbox output | Retain readiness/test screen and safe neutral cleanup |
| `src/Tippy.App/Services/MidiOutputService.cs` | MIDI output | Retain named device selection, test, lazy open, and validation |
| `src/Tippy.Core/Output/MidiMessageParser.cs` | MIDI parser | Reuse parser and tests; plan a separate MIDI-input trigger model |
| `src/Tippy.App/Services/OscOutputService.cs` | OSC output | Retain presets/test and lazy resources |
| `src/Tippy.App/Services/PowerShellCommandService.cs` | PowerShell output | Preserve current-user/no-bypass rules; add explicit cancellation/timeout coverage |
| `src/Tippy.App/Services/MacroVariableExpander.cs` | Variables | Rename `{pedal}`/`{bank}` aliases to `{control}`/`{layer}` while migrating old aliases safely |
| `src/Tippy.App/Services/ApplicationShortcutCatalog.cs` | App shortcut browser | Reuse catalog data and organization without Tippy branding |
| `src/Tippy.App/Services/WindowsActionCatalog.cs` | Windows shortcut catalog | Reuse with scan-code-aware individual-key catalog |
| `src/Tippy.App/Services/InstalledApplicationScanner.cs` | Permission-gated app discovery | Preserve local-only scope, review list, and duplicate protection |
| `src/Tippy.App/Services/ForegroundApplicationService.cs` | Application scenes | Keep event-driven resolution on input; no polling loop |
| `src/Tippy.App/Services/ThemeService.cs` | TerkWerX light/dark theme | Apply Tappy resources and accessibility contrast tests |
| `src/Tippy.App/Services/WindowPlacementService.cs` | Window persistence | Preserve per-layout sizes/current-monitor anchoring; extend to full-keyboard canvas states |
| `src/Tippy.App/Services/StartupRegistrationService.cs` | Start with Windows | Use unique `Tappy` registry value and executable path |
| `src/Tippy.App/Services/UpdateService.cs` | Optional GitHub update check | Point only to `TerkWerX/TAPPY`; retain opt-in/no-telemetry behavior |
| `src/Tippy.App/Services/CrashRecoveryService.cs` | Recovery/logging | Use Tappy data root; ensure reports exclude keystrokes and macro contents |
| `src/Tippy.App/Services/ProfileStore.cs` | Profile/backups/portable mode | New extensions, paths, schema, and conversion boundary |
| `src/Tippy.App/Services/GlobalHotkeyService.cs` | Emergency/layer hotkeys | Guarantee an unaffected escape route when mapping a full keyboard |

## Adapt substantially

| Tippy reference | Tappy replacement | Why it cannot be copied unchanged |
|---|---|---|
| `PedalBank`, `PedalBinding`, `PedalDeviceProfile` | `InputLayer`, `ControlBinding`, `ControllerProfile` | Must support far more controls and configurable layer counts |
| `PedalGestureEngine` | `ControlGestureEngine` | Keyboard autorepeat, dual-role keys, one-shot modifiers/layers, and layout semantics |
| `PedalPatternEngine` | `ChordSequenceEngine` | Many-key rollover, leader sequences, overlap/conflict resolution, and key repeats |
| `PedalBankResolver` | `LayerResolver` | Momentary/toggle/one-shot/app layers and nested ownership |
| `ApplicationProfileRule`/scenes | Controller application scenes | Scenes may address groups/entire layouts and must scale beyond three controls |
| `RawInputService` | Device-specific keyboard provider | Model scan code + E0/E1 + make/break + HID usage, not only `VirtualKey`; ignore self-injection/repeats correctly |
| `HidLearningService` and learned definitions | Raw-HID controller learner | Raise 1–32 limit where appropriate; support keyboard usages, consumer controls, NKRO, encoders later |
| `PedalRegistryService` | `ControllerRegistryService` | Registry needs physical key geometry, capabilities, layout variants, orientation, and input-provider type |
| `DeviceSupportPackService` | Authenticated controller packs | New schema/extension/trust store; retain data-only and signing protections |
| `SupportReportService` | Tappy support reports | Never include normal typed content; hash device paths and bound raw event samples |
| `TippyDoctorService` | Tappy Doctor | Add pass-through mode, primary-keyboard protection, injected-loop test, rollover, and layout integrity |
| `HardwareCertificationSession` | Controller Passport | Test every key/representative groups, make/break, rollover, repeat, identity, reconnect, and cleanup |
| `HardwareLoopbackSession` | Tappy HIL station | Add two-keyboard isolation and, later, mechanical/electrical keyboard fixtures |
| Main window pedal cards | Scalable controller canvases | Support 17-key numpads through 105-key layouts without top-level scrollbars |
| Compact/¼ pedal views | Compact controller/overlay views | A full keyboard needs zoom-to-fit, groups, and selected-device focus |

## Replace entirely

- `InfinityReportDecoder`, `GenericThreeSwitchDecoder`, and Infinity/AltoEdge protocol
  assumptions.
- `PedalHidService` as the primary runtime input source. Tappy now explicitly
  composes its Raw Input keyboard and model-specific Logitech G13 providers; a
  general learned-HID/MIDI/joystick coordinator and composite identity model remain
  future work.
- Hard-coded left/center/right or three-switch visual geometry.
- Pedal-specific filenames, product copy, compatibility claims, support URLs, data
  paths, profile extensions, mutexes, installer GUIDs, registry keys, icon IDs, and
  Tippy mascot assets.
- Any assumption that one HID report byte or a 32-bit mask can represent all controls.
- Tippy's `RawInputKeyEvent` shape, path-only keyboard identity, WPF-dispatcher input
  routing, and zero-valued `SendInput.ExtraInfo`. Tappy needs stable `ControlId`
  dictionaries for 100+ controls, a UI-independent input path, stronger device
  identity, and tagged self-injection from its first vertical slice.

## Important Tippy lessons to preserve

1. **Held state belongs to a physical owner.** Shared modifiers and buttons use
   reference counts; unplug/lock/suspend/exit synthesize releases.
2. **Input is event driven.** Async reads and Windows messages do the normal work;
   reconnect retries back off and foreground scenes resolve only on input.
3. **Unknown hardware is learned as data.** Do not turn community packs into an
   executable plug-in system.
4. **Hardware appearance is not protocol evidence.** Shared VID/PID or a familiar
   shell may require a user choice and an “unverified” label.
5. **Every layout has independent remembered geometry.** Switching arrangements
   must not recenter, jump monitors, shrink below usable content, or introduce window
   scrollbars.
6. **Compact modes are explicit modes.** They are not accidental results of making
   the normal window too small, and they always expose a reliable return path.
7. **Theme contrast is tested.** Combo boxes, tab headers, checkboxes, selected
   items, disabled text, and dialogs need light/dark/high-contrast coverage.
8. **Dangerous actions are honest.** PowerShell never elevates or bypasses policy;
   imported active content gets a safety preview.
9. **Diagnostics are privacy bounded.** Aggregate state and timing are useful; a
   key-code sequence can itself reconstruct typing. Raw key samples require a short,
   visibly armed capture and do not belong in default support reports. Typed text,
   clipboard data, macro bodies, user names, and raw device paths are never included.
10. **Packaging is part of correctness.** Test the published ZIP/installer and every
    file-path-loaded registry/artwork asset, not merely the build output.
11. **Release identity is unique.** Tappy gets new paths, GUIDs, mutexes, URLs, icons,
    issue templates, update feed, and crash-report destination.
12. **Separate publication scopes.** The owner authorized Tappy source,
    documentation, and CI in the public repository. That does not authorize a
    packaged release, signing, website upload, production sync, or a public software
    license. The owner separately approved the three supplied Tappy brand images for
    in-app use on 2026-09-03; that approval does not extend to Tippy mascot assets.

## New keyboard-specific failure modes to prevent

- Mapping output recursively triggering its own input.
- Selecting or logging the user's normal keyboard unintentionally.
- Claiming per-device suppression when original keystrokes still pass through.
- Losing the only emergency-stop key after mapping an entire keyboard.
- Confusing numpad keys with navigation keys when Num Lock changes.
- Collapsing left/right modifiers, extended Enter, OEM keys, or consumer controls.
- Treating OS autorepeat as multiple physical taps or double taps.
- Dropping simultaneous keys on NKRO/6KRO devices or inventing rollover support.
- Stranding modifiers after hot-unplug, sleep, lock, crash, or layer change.
- Identical controllers swapping profiles after reconnect/USB port changes.
- UI attempting to render a 104-key keyboard at pedal-card dimensions.
- A global hook intercepting every keyboard when only one spare numpad was selected.
- Driver-based exclusive mode leaving the user without input after a crash.
- Imported macros silently launching programs, PowerShell, or network actions.

## Baseline tests worth porting first

- `tests/Tippy.Core.Tests/HeldOutputLedgerTests.cs`
- `tests/Tippy.Core.Tests/PedalGestureEngineTests.cs` (rename/generalize)
- `tests/Tippy.Core.Tests/PedalPatternEngineTests.cs` (rename/generalize)
- `tests/Tippy.Core.Tests/ProfileTests.cs`
- `tests/Tippy.App.Tests/ReliabilityAndPortabilityTests.cs`
- `tests/Tippy.App.Tests/CatalogOrganizationTests.cs`
- `tests/Tippy.App.Tests/LayoutFitCalculatorTests.cs`
- `tests/Tippy.App.Tests/PowerShellCommandServiceTests.cs`
- `tests/Tippy.App.Tests/AdvancedOutputsAndSupportPackTests.cs`
- `tests/Tippy.App.Tests/SupportReportServiceTests.cs`
- `tests/Tippy.App.Tests/TippyDoctorAndApplicationDiscoveryTests.cs`

Port the assertion intent, not pedal-specific class names or fixed counts. Add the
Raw Input, scan-code, pass-through, recursion, rollover, dual-role, and primary-
keyboard protection tests listed in `PROJECT_TEMPLATE.md` before calling the first
vertical slice complete.
