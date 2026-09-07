# Testing and evidence record

This record separates deterministic software evidence from packaged-artifact and
physical-hardware evidence. Passing automated tests does not certify a controller,
prove 6KRO/NKRO behavior, or establish physical latency.

## 2026-09-07 source-tree verification

Environment: Windows NT `10.0.26200.0`, x64, .NET SDK `10.0.400`; product projects
target .NET 8. The verification was run in Release configuration.

| Check | Result | Evidence boundary |
|---|---:|---|
| `dotnet build Tappy.slnx -c Release` | Passed; 0 warnings, 0 errors | Current local source tree |
| `Tappy.Core.Tests` | 58 passed, 0 failed | Deterministic platform-neutral behavior, including exclusive-input safety policy, explicit cleanup-dispatch results, immutable action sequences, profile/layout round-trip, MIDI parsing, OSC encoding, and action-output press/release/Rehearsal routing |
| `Tappy.Windows.Tests` | 135 passed, 0 failed | Keyboard/G13/MIDI packet parsing and providers, exact G13 lighting isolation, duplicate-startup-arrival suppression, ContainerId grouping, output and lifecycle seams, plus 20 filter-protocol, device-session, and grouped two-phase coordinator cases using deterministic/native-boundary fixtures |
| `Tappy.App.Tests` | 109 passed, 0 failed | Keyboard/G13/MIDI selection and routing, persistent freeform controller layouts, group selection/coloring, model-aware lighting palettes and photo locators, assignment editing, theme readability, profile round-trip, and lifecycle/fault cleanup with fake providers/output |
| `Tappy.InputBroker.Tests` | 16 passed, 0 failed | Bootstrap SID/key refusal, protected pipe ACL, bounded HMAC framing, modification/key/replay/reserved/size rejection, real local named-pipe handshake, and status-only command boundary |
| `Tappy.G13Hil.Tests` | 25 passed, 0 failed | Finite state machine, retriable operator slips, physical-stick perpendicular crossings, explicit-arm/argument refusal, exact-device gating, interruption handling, and aggregate/redacted evidence contract |
| `Tappy.OutputWitness.Tests` | 53 passed, 0 failed | Exact-arm refusal, finite focused-console make/repeat/break and output state machines, quiet/post-release observation windows, aggregate-only evidence, cleanup, and privacy boundaries |
| Current automated total | 396 passed, 0 failed | Core 58 + Windows 135 + App 109 + Input Broker 16 + G13 HIL tool 25 + Output Witness 53 |
| `dotnet list Tappy.slnx package --vulnerable --include-transitive` | Exit 0; no known vulnerable packages reported in all 14 projects | Point-in-time NuGet advisory data from `nuget.org`; not a complete security audit |
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
redaction; the app's safe F13-F24 milestone path; the searchable keyboard assignment
catalog with more than 1,500 direct/modifier choices; tap, held, and release-trigger
assignment projection; bounded multi-step action schema and MIDI/OSC syntax/packet
contracts; action-output press/release/Rehearsal routing; identification-time WPF key handling;
live automation names; ordered deferred visual transitions; a truthful quick-tap
illumination pulse; bounded backlog compaction with final-state preservation;
presentation-mode minimums; and high-contrast theme
precedence. Added coverage proves authoritative keyboard ContainerId grouping and
the dedicated G13 decoder/provider/App path, including exact identity, `C232`
exclusion, all code-defined controls, simultaneous state, profile round-trip, and
fail-safe cleanup. The G13 locator tests also prove that all 39 supported controls
have unique bounded hotspots, unknown identities receive no photo, and grid clicks
plus physical presses reference the same visual state. Those are code tests, not
physical G13 control or hotspot-alignment evidence.

The Output Witness tests cover its narrow allowlist, explicit acknowledgments,
aggregate-only evidence, exact selected-output cardinality, source repeat, and
post-condition drains. They do not provide physical-device attribution or replace
the attended operator record.

The app suite also asserts that both standard theme palettes provide at least 4.5:1
ComboBox and disabled-button text/background contrast, that High Contrast uses
Windows system colors, that the device dropdown overrides the global TextBlock
foreground which made unselected device names unreadable during the first attended
T01 attempt, that the expanded assignment results are virtualized and searchable,
and that long control/Rehearsal labels wrap rather than clip. After the first
expanded-editor visual pass exposed pale inherited text on native white list and
selector surfaces, dedicated assignment-list resources were added: standard-theme
title, description, and selected-row pairs are now mechanically held to at least
7:1 contrast, while High Contrast routes through Windows system colors. The action
tabs now bind explicit header foreground/background brushes, and the tab content,
result count, and sequence list bind explicit dark-panel or assignment-list
surfaces rather than inheriting colors from native WPF controls.

The build above validates the current source tree. Source, documentation, and CI are
authorized for the public repository. Clean-checkout CI and every local package run
must generate their own revision/payload manifest; tracked docs intentionally do not
duplicate a commit ID that would become stale when the record itself is committed.

## Local portable artifact checkpoint

The last post-provider package checkpoint was built from clean committed source and
predates the expanded assignment editor. It ran all 261 tests, recorded all twelve package locks, verified the allowlisted
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

No complete Controller Passport, finite G13 HIL session, or Targus T01–T12 run has
been completed. The operator did select/identify/confirm the G13 in the final
accessibility build and reported that all of its controls responded visually in
Tappy; the local ignored record preserves that statement and screenshot without
promoting it into a formal output or pass-through result. In particular, the
following remain unverified on hardware:

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
suppression. Its 25 deterministic tests pass. On 2026-09-07 the exact attached
`046D:C21C`, `FF00:0000` G13 completed the armed live run with all aggregate
assertions true, 78 completed control cycles, 95 balanced accepted presses/releases,
and zero unexpected, duplicate, unbalanced, disconnect, fault, or lifecycle events.
The evidence SHA-256 is
`54CE558B9180D541E647D745C214CA778882D0B8C03815B7ED751AB7EE023A86`.
This input-functional record does not advance the G13 beyond code-supported by
itself. See the
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

Repository visibility and the supplied Tappy in-app brand set are authorized, but no
public software license has been selected or granted. The configured `/tappy/`
website URL is an identity reservation, not a live-site claim; the endpoint returned
404 in the 2026-09-02 audit. Packaged release, signing, website publication, and
production hosting remain separate owner decisions.

An owner-authorized internal donor audit found no reusable G13 or SpacePilot
implementation. No donor code, repository history, or logs were copied. The Tappy
G13 path was independently implemented from the public sources recorded in
[`LOGITECH_G13.md`](LOGITECH_G13.md) and project-owned design.

## Optional exclusive-input evidence boundary

Twelve deterministic Core cases currently prove the accepted activation policy:
primary and secondary roles must be distinct and unambiguous; recovery paths,
administrator consent, HVCI, production trust, normal boot configuration, current
heartbeat, and both driver acknowledgements are mandatory; protected-application
activity refuses activation. Twenty Windows cases freeze unique read/write
buffered control codes, watchdog bounds, protocol versioning, wire layout, two-phase
multi-interface role acknowledgement, and ordered bounded event-batch rules. Sixteen broker cases
exercise user/key validation, pipe ACLs, HMAC/replay/size rejection, a real local
pipe exchange, and the status-only command boundary.

Those tests are contracts, not runtime driver evidence. An unsigned SYS/INF/CAT lab
scaffold now builds with WDK code analysis clean, but it has never been installed or
loaded. A status-only broker executable builds and self-tests, but no service,
installer feature, or exclusive-input UI is installed. No
test-signing, Driver Verifier, HVCI, HLK, anti-cheat, upgrade/rollback, Safe Mode, or
watchdog-kill physical run has occurred. The effective mode therefore remains
pass-through. The complete gates are in
[`EXCLUSIVE_KEYBOARD_INPUT.md`](EXCLUSIVE_KEYBOARD_INPUT.md) and
[`DRIVER_SIGNING_AND_RELEASE.md`](DRIVER_SIGNING_AND_RELEASE.md).

## Next milestone

Use the single-interface Targus numberpad as the simplest first Raw Input/manual
vertical-slice witness, then test the K15 and Tartarus through the same app workflow.
Run the explicitly armed G13 verifier and retain a passing aggregate HIL record.
Complete Controller Passport/HIL input and pass-through checks before promoting any
device. Re-run the
portable audit and clean-checkout CI after the provider additions, and continue to
repeat the dependency advisory query at release checkpoints. Keep generic layouts
for other devices: their processed controller images remain excluded pending source
provenance, usage rights, exact-model/protocol evidence, processing records, and
explicit human approval. The owner-supplied G13 locator is approved narrowly for
the exact implemented G13 identity.
