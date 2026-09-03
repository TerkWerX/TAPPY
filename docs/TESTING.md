# Testing and evidence record

This record separates deterministic software evidence from packaged-artifact and
physical-hardware evidence. Passing automated tests does not certify a controller,
prove 6KRO/NKRO behavior, or establish physical latency.

## 2026-09-03 source-tree verification

Environment: Windows NT `10.0.26200.0`, x64, .NET SDK `10.0.303`; product projects
target .NET 8. The verification was run in Release configuration.

| Check | Result | Evidence boundary |
|---|---:|---|
| `dotnet build Tappy.slnx -c Release` | Passed; 0 warnings, 0 errors | Current local source tree |
| `Tappy.Core.Tests` | 31 passed, 0 failed | Deterministic platform-neutral behavior, including explicit cleanup-dispatch results |
| `Tappy.Windows.Tests` | 99 passed, 0 failed | Keyboard/G13 packet parsing, ContainerId grouping, capability-isolated registration faults, bounded message-host shutdown routing, provider contracts, pre-arm neutrality, output tagging, storage, redaction, identity, and lifecycle seams using deterministic/native-boundary fixtures |
| `Tappy.App.Tests` | 55 passed, 0 failed | Keyboard/G13 selection, ordered/deferred UI state projection, quick-tap illumination, bounded visual compaction, mapping/profile round-trip, Rehearsal Mode, truthful cleanup-failure and unclean-session handling, serialized input/disposal, persistent recovery warnings, lifecycle/fault cleanup, readable dropdown/disabled-button/control-label themes, accessibility state, and unplug handling with fake providers/output |
| `Tappy.G13Hil.Tests` | 23 passed, 0 failed | Finite state machine, explicit-arm/argument refusal, exact-device gating, interruption handling, and aggregate/redacted evidence contract |
| `Tappy.OutputWitness.Tests` | 53 passed, 0 failed | Exact-arm refusal, finite focused-console make/repeat/break and output state machines, quiet/post-release observation windows, aggregate-only evidence, cleanup, and privacy boundaries |
| Current automated total | 261 passed, 0 failed | Core 31 + Windows 99 + App 55 + G13 HIL tool 23 + Output Witness 53 |
| `dotnet list Tappy.slnx package --vulnerable --include-transitive` | Exit 0; no known vulnerable packages reported in all 12 projects | Point-in-time NuGet advisory data from `nuget.org`; not a complete security audit |
| `dotnet format Tappy.slnx --verify-no-changes --no-restore` | Passed | Current local source tree |

The automated slice covers explicit neutral-state gating plus
select/identify/release/confirm activation;
scan-code plus E0/E1 identity; make, break, repeat, simultaneous and multi-session
state; unselected and self-injected input rejection; reference-counted held output;
frozen release context; disconnect, lifecycle, profile-swap, and emergency cleanup;
truthful latching and re-arm refusal when an owned-output release is rejected;
bounded native message-host shutdown; optional G13-capability fault isolation;
serialized input/disposal and conservative unclean-session recovery;
recursion/depth/rate guards; immutable profile round-trip and isolation; raw-path
redaction; the app's safe F13-F24 mapping path; identification-time WPF key handling;
live automation names; ordered deferred visual transitions; a truthful quick-tap
illumination pulse; bounded backlog compaction with final-state preservation;
presentation-mode minimums; and high-contrast theme
precedence. Added coverage proves authoritative keyboard ContainerId grouping and
the dedicated G13 decoder/provider/App path, including exact identity, `C232`
exclusion, all code-defined controls, simultaneous state, profile round-trip, and
fail-safe cleanup. Those are code tests, not physical G13 control evidence.

The Output Witness tests cover its narrow allowlist, explicit acknowledgments,
aggregate-only evidence, exact selected-output cardinality, source repeat, and
post-condition drains. They do not provide physical-device attribution or replace
the attended operator record.

The app suite also asserts that both standard theme palettes provide at least 4.5:1
ComboBox and disabled-button text/background contrast, that High Contrast uses
Windows system colors, that both visible dropdowns override the global TextBlock
foreground which made unselected device names unreadable during the first attended
T01 attempt, and that long control/Rehearsal labels wrap rather than clip.

The build above validates the current source tree. Source, documentation, and CI are
authorized for the public repository. Clean-checkout CI and every local package run
must generate their own revision/payload manifest; tracked docs intentionally do not
duplicate a commit ID that would become stale when the record itself is committed.

## Local portable artifact checkpoint

The current post-provider package checkpoint was built from clean committed source.
It ran all 261 tests, recorded all twelve package locks, verified the allowlisted
three-file payload, and executed both the actual published `Tappy.exe` and a fresh
copy extracted from the portable ZIP. Each readiness run passed
`controller-registry`, `profile-round-trip`, `rehearsal-no-output`, and
`tappy-doctor`, with `injectedInputCount: 0`. The manifest records the source as not
dirty and includes the exact source revision, payload and archive hashes.

The generated manifest is authoritative for the exact source revision, payload
paths/sizes/hashes, archive hash, toolchain, and unsigned status. A passing local
readiness artifact is not an authorized software release.

## Physical and manual evidence

The finite attended procedure is recorded in
[`FIRST_MILESTONE_OPERATOR_RUN.md`](FIRST_MILESTONE_OPERATOR_RUN.md). It separates
the binding one-Targus milestone witness from broader K15/Tartarus/G13 Passport and
HIL promotion. Copy the
[`attended evidence template`](FIRST_MILESTONE_RECORD_TEMPLATE.md) into the ignored
run directory; the procedure and blank template are not evidence that any step
passed.

No Controller Passport, G13 HIL session, or operator-reviewed physical control run
has been completed. In particular, the following remain unverified on hardware:

- make/break illumination, OS repeat, rollover/ghosting, simultaneous-state truth,
  Num Lock variants, reconnect identity, and identical-device selection;
- original-key pass-through in a harmless target application;
- normal F13-F24 output, self-injection behavior, unplug-while-held release, the
  global emergency chord, and mouse/tray recovery;
- Windows lock/unlock, suspend/resume, orderly shutdown, multi-monitor/DPI behavior,
  theme/high-contrast accessibility, and input-to-output latency targets.

The latest schema-3 descriptor-only `Tappy.DeviceProbe` completed with exit code 0.
It reported eight logical controllers: seven keyboard groups and one
supported-controller group. The attached, user-identified Freewolf K15 candidate is one authoritative
ContainerId group at VID `1A2C`/PID `2D43`, with four Raw Input keyboard interfaces
and distinct reported total-key capabilities of 56 and 264. The user-identified
Targus numberpad candidate is one `05A4:9862` keyboard interface with reported total
264. Windows identifies the Razer Tartarus at `1532:0201`; Tappy groups its two Raw
Input keyboard interfaces and reports total 264. Windows exposes additional
Tartarus mouse, consumer-control, system-control, and vendor-HID collections outside
the current generic keyboard provider. The physical Logitech G13 is exactly one
`046D:C21C`, `FF00:0000` ContainerId group with one interface and 39 code-defined
controls. `046D:C232` does not appear because it is the excluded G HUB virtual
keyboard, not G13 identity.

The probe registered no input, opened no reports, captured no control activity, and
printed no raw paths or ContainerIds. These are Enumerated/code-supported facts only:
they establish neither K15/Targus/Tartarus key behavior nor G13 button/stick
behavior, mappings, unplug recovery, image identity, Functional status, or Verified
support. Follow
[`HARDWARE_TEST_STATION.md`](HARDWARE_TEST_STATION.md) before promotion.

`Tappy.G13Hil` now provides a finite, explicitly armed aggregate verifier for all 39
code-defined controls, simultaneous groups, transition balance, and duplicate
suppression. Its 23 deterministic tests pass, but no armed live run has completed;
its existence does not advance the G13 beyond code-supported. See the
[G13 support boundary](LOGITECH_G13.md).

## Static identity and privacy audit

A case-insensitive source-tree scan (excluding build output, artifacts, and the
separately managed `PAD IMAGES` tree) found no Tippy executable/data/profile/
installer identity, old installer GUID, old mutex, or old Tippy URL in Tappy runtime
or packaging values. The canonical Tappy values agree across
`eng/product-identity.json`, Windows constants, tests, and installer scaffolding.

Occurrences of `Tippy` or `pedal` are intentional and limited to the kickoff/source
specifications, the read-only reuse map and architecture provenance, coexistence or
non-reuse warnings, the README sister-project acknowledgement, and negative identity
assertions in tests. They are documentation/test context, not executable identity.
No identity correction was required.

The same audit found no telemetry or upload implementation. Normal diagnostics are
aggregate-only; raw device paths are converted to sanitized fingerprints at the
Windows boundary and are covered by redaction tests.

Repository visibility is authorized, but no public software license has been
selected or granted. The configured `/tappy/` website URL is an identity reservation,
not a live-site claim; the endpoint returned 404 in the 2026-09-02 audit. Packaged
release, signing, website publication, production hosting, and final branding remain
separate owner decisions.

An owner-authorized internal donor audit found no reusable G13 or SpacePilot
implementation. No donor code, repository history, or logs were copied. The Tappy
G13 path was independently implemented from the public sources recorded in
[`LOGITECH_G13.md`](LOGITECH_G13.md) and project-owned design.

## Next milestone

Use the single-interface Targus numberpad as the simplest first Raw Input/manual
vertical-slice witness, then test the K15 and Tartarus through the same app workflow.
Run the explicitly armed G13 verifier and retain a passing aggregate HIL record.
Complete Controller Passport/HIL input and pass-through checks before promoting any
device. Re-run the
portable audit and clean-checkout CI after the provider additions, and continue to
repeat the dependency advisory query at release checkpoints. Keep generic
code-rendered layouts: all processed controller images remain excluded pending
source provenance, usage rights, exact-model/protocol evidence, processing records,
and explicit human approval.
