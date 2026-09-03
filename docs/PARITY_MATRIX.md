# Tippy-to-Tappy parity matrix

Status values are `Implemented`, `First-slice`, `Code-supported`, `Planned`,
`Deferred`, or `Hardware evidence required`. "Source" records conceptual
provenance, not a runtime dependency; Tappy does not reference the Tippy working
tree.

Evidence snapshot: the Release solution build has zero warnings/errors and all 287
current automated tests pass (Core 46, Windows 103, App 62, G13 HIL tool 23,
Output Witness 53). The
last clean-source portable checkpoint includes the K15/G13 provider additions and
passes both published/extracted readiness audits; it predates the expanded keyboard
assignment editor. Exact evidence boundaries are in [`TESTING.md`](TESTING.md).
Formal Targus and finite G13 output/pass-through checks remain pending; an attended
preflight separately records the operator's report that every G13 control responds
visually.
The finite attended run and its narrow-versus-promotion boundary are defined in
[`FIRST_MILESTONE_OPERATOR_RUN.md`](FIRST_MILESTONE_OPERATOR_RUN.md); publishing the
procedure does not advance any hardware tier.

| Capability | Status | Source / Tappy adaptation | Automated evidence | Remaining evidence or work |
|---|---|---|---|---|
| Multiple physical controllers | First-slice | New session/persistent identity, per-device state, and authoritative ContainerId interface grouping | Multi-session isolation plus K15 four-interface grouping/reconnect tests | Two real devices, identical pair/rebind, and physical K15 control evidence |
| Configurable independent layers | First-slice | Generalized from Tippy banks; variable-length schema, three defaults | Profile/layer tests | Full UI switching and portable layer preview |
| Portable layer save/load/copy | Planned | ControlId compatibility rather than control count | Compatibility validator planned | Preview UI and differing-layout tests |
| Keyboard keys and chords | First-slice | Native searchable editor with named Windows actions, direct keys, media/browser keys, and generated modifier combinations | More than 1,500 catalog choices validated against the complete Windows output translation contract; balanced chord engine tests | Physical output-witness checks and future locale/scan-code choice refinements |
| Keyboard sequences/text/timed recording | First-slice | Controller-native ordered step model and Unicode output | Immutable snapshot, ordered routing, editor, and bounded-execution tests | Timed input recorder and per-step editing |
| Mouse/program outputs | First-slice | Marked mouse injection and current-user shell launch | Schema/editor validation plus owned scheduler cleanup | Physical output checks and richer drag/path picker UI |
| PowerShell 5.1/7 output | First-slice | Hidden `-NoProfile -NonInteractive`; no elevation or policy bypass | Host/sequence validation and bounded scheduler | Physical command test in a disposable target and captured stdout/error UX |
| MIDI/OSC output and presets | First-slice | `winmm` short messages plus typed OSC/UDP packets | MIDI range/packing, OSC encoding, editor and action-routing tests | Real endpoint HIL, reusable presets, and test-send UI |
| Virtual Xbox output | Deferred | Optional adapter, never bundled driver | None | Driver review and real output evidence |
| Variables and previews | Planned | `{control}`/`{layer}` native vocabulary | Parser tests planned | Manager UI |
| Press/release/run-once/hold | First-slice | Frozen execution leases and held ledger; editor exposes press/release/held behavior for all current step types | Press/release/chord/action-sequence/cleanup plus editor-projection tests | One-dialog independent press and release sequence editing |
| Repeat/toggle/tap/double/long | First-slice | Bounded repeat-while-held exists; gesture engine remains planned | Repeat classification and sequence routing tests | Toggle/double/long gesture/conflict UI and timing tests |
| Momentary/toggle/one-shot layers, dual role | Planned | New layer ownership model | Schema only | Engine/UI/conflict tests |
| Cross-device chords | Planned | Explicit additive/consuming policy | Model tests planned | Visible consuming delay and HIL |
| Ordered/leader sequences | Planned | Stable ControlId histories, bounded/opt-in only | None | Privacy-safe capture and engine |
| Reference-counted shared held output | First-slice | Keyboard ledger plus action-execution ownership | Shared-owner and release tests; held MIDI notes synthesize note-off | Cross-sequence mouse/MIDI reference counts and gamepad expansion |
| Cleanup on unplug/lifecycle/emergency | First-slice | One owner-release path for keyboard and action scheduler | Disconnect/profile/emergency/action-sequence tests | Physical lock/suspend/unplug HIL |
| Freeze key-down context through release | First-slice | New immutable execution lease | Mid-hold profile/layer tests | Application-scene coverage |
| Foreground application scenes | Planned | Resolve only on input, no polling | Interface/model tests planned | Full scene editor and Windows tests |
| Permission-gated application discovery | Planned | Local installed/Start Menu/visible process review | None | Implement; no documents/history/data |
| Windows/application catalogs | First-slice | Native searchable Windows/key catalog with virtualized results and more than 1,500 choices; Tippy's catalog organization was deliberately adapted | Catalog count/category/key-contract/search UI tests | Port and review the 557-command application-specific catalog |
| MIDI/OSC setup and variable manager | First-slice | Per-assignment MIDI device and OSC endpoint fields | Enumeration/parser/packet/editor tests | Reusable named presets, test-send controls, and variables |
| Rehearsal Mode | First-slice | Mapping path runs with output suppressed | Core/App tests plus current published/extracted-package smoke with zero injected input | Physical visual check |
| Emergency stop and output bounds | First-slice | Unique hotkey plus mouse/tray commands; depth/rate, 500-step, 30-second, and 20-second-repeat limits | Safety and action-routing tests | Native-output race fixture plus hotkey conflict/physical recovery test |
| Tray/background/startup | First-slice | Unique tray identity and recovery; startup deferred | App compiles; no automated tray interaction test | Tray/background manual test; startup settings UI |
| Themes and responsive modes | First-slice | Family tokens, code-rendered placeholder brand | App tests cover mode minimums, live automation names, and high-contrast theme precedence; WPF resources compile | Manual light/dark/high-contrast, DPI, and layout-mode accessibility checks |
| Backups/rollback/portable/recovery | First-slice | Atomic profile, LKG/quarantine; isolated smoke root | Store tests plus published/extracted profile round-trip smoke | Full UI and crash-recovery workflow |
| Updates without telemetry | Deferred | Future Tappy-only opt-in endpoint | None | Public release authorization |
| Immutable snapshots/migrations | First-slice | Schema v2 adds action sequences while normalizing v1 profiles | Deep action-sequence snapshot, round-trip, and corrupt isolation tests | Explicit migration fixtures for later versions |
| Live diagnostics/overlay | First-slice | Aggregate selected-device state with ordered bounded visual delivery | Redaction/state, quick-tap pulse, FIFO, and backlog-compaction tests | Overlay and bounded armed samples |
| Tappy Doctor | First-slice | Headless readiness runner implemented | Current clean post-provider published-directory and fresh-ZIP runs passed all four checks with zero injected inputs | Full interactive Doctor |
| Controller Passport/HIL | Hardware evidence required | General station plan, finite G13 input verifier, and focused-console output witness | 23 G13-verifier plus 53 output-witness state-machine/refusal/evidence tests; no physical capture | Attended Targus milestone record, armed G13 input-functional record, and full controller Passport/output/pass-through/latency HIL |
| Signed data-only controller packs | Planned | New extension/schema/trust store | Fixture only | Auth/sign/install/catalog implementation |
| Raw keyboard provider | First-slice | Scan/E0/E1/device identity, dedicated message thread, and ContainerId grouping | Normalizer/provider contracts plus authoritative K15 four-interface and Tartarus two-interface grouping | Physical K15/Targus/Tartarus make-break, rollover, pass-through, reconnect, and cleanup |
| Logitech G13 vendor-HID provider | Hardware evidence required | Exact `046D:C21C`, `FF00:0000` provider; `C232` excluded; fixed 39-control model, stable tile grid, and separate live photo locator | Decoder/provider/App tests, exact 39-hotspot catalog and shared-state tests, plus 23 finite-verifier tests; attended operator report that every control responds visually | Complete the finite armed input record and broader Passport/output/pass-through HIL; the operator report alone is not a promotion pass |
| Learned raw-HID provider | Planned | Core has a discrete provider seam with no fixed 32-control cap; current App composition is explicitly keyboard plus model-specific G13 | Interface and G13-specific composition only | Generic identity, selection/UI integration, learner, report schema, and real-device tests |
| MIDI/encoder/joystick triggers | Planned | Discrete Core seam; G13 has provider-specific raw X/Y and fixed-threshold directions, not a generic analog profile model | Interface plus model-specific G13 direction tests | Add generic analog values/threshold/deadzone schemas, integrate new providers, and hardware-test |
| Data-driven controller layouts | First-slice | Generic grid/registry plus a code-defined 39-control G13 model; selectable tiles stay separate from an exact-identity photo locator | Profile tests round-trip more than 100 controls; App tests cover G13 model/grid/photo shared state and all 39 bounded hotspots | Row/cluster-aware WPF projection, designer/templates, and additional reviewed device art |
| Batch/drag/compare/learn-all/search | Planned | Tappy-specific mapping workflows | None | Implement |
| Source behavior/rollover/conflict pages | First-slice | Pass-through truth and live simultaneous state | State/repeat/simultaneous tests | Dedicated rollover/conflict screens and HIL |
| Usage heatmaps | Deferred | Local-only ControlId counts, disabled by default | None | Privacy review and opt-in UI |
| Final branding/artwork | Deferred | Placeholder branding; one approved owner-photo G13 functional locator | Embedded G13 resource and hotspot tests; unrelated `PAD IMAGES` remain excluded | Final brand approval and provenance/approval for any additional device art |
