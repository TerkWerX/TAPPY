# Tippy-to-Tappy parity matrix

Status values are `Implemented`, `First-slice`, `Code-supported`, `Planned`,
`Deferred`, or `Hardware evidence required`. "Source" records conceptual
provenance, not a runtime dependency; Tappy does not reference the Tippy working
tree.

Evidence snapshot: the Release solution build has zero warnings/errors and all 184
current automated tests pass (Core 31, Windows 92, App 38, G13 HIL tool 23). The
current clean-source portable checkpoint includes the K15/G13 provider additions
and passes both published/extracted readiness audits. Exact evidence boundaries are
in [`TESTING.md`](TESTING.md). All
physical/manual control checks remain pending.

| Capability | Status | Source / Tappy adaptation | Automated evidence | Remaining evidence or work |
|---|---|---|---|---|
| Multiple physical controllers | First-slice | New session/persistent identity, per-device state, and authoritative ContainerId interface grouping | Multi-session isolation plus K15 four-interface grouping/reconnect tests | Two real devices, identical pair/rebind, and physical K15 control evidence |
| Configurable independent layers | First-slice | Generalized from Tippy banks; variable-length schema, three defaults | Profile/layer tests | Full UI switching and portable layer preview |
| Portable layer save/load/copy | Planned | ControlId compatibility rather than control count | Compatibility validator planned | Preview UI and differing-layout tests |
| Keyboard chords/sequences/text/timed recording | Planned | Macro model behind output interfaces | Model validation only | Execution/editor and balanced recording |
| Mouse/program outputs | Planned | Tippy concepts split from platform-neutral core | Interfaces only | Implement and safety test |
| PowerShell 5.1/7 output | Planned | Preserve non-elevated/no-bypass policy | Interface/import safety only | Timeout/cancel service and tests |
| MIDI/OSC output and presets | Planned | Adapt Tippy services; lazy resources | Parser/endpoint tests planned | Real endpoints and UI |
| Virtual Xbox output | Deferred | Optional adapter, never bundled driver | None | Driver review and real output evidence |
| Variables and previews | Planned | `{control}`/`{layer}` native vocabulary | Parser tests planned | Manager UI |
| Press/release/run-once/hold | First-slice | Frozen execution leases and held ledger | Press/release/cleanup tests | Broader output types |
| Repeat/toggle/tap/double/long | Planned | Keyboard-aware gesture engine | Repeat classification in first slice | Gesture/conflict UI and timing tests |
| Momentary/toggle/one-shot layers, dual role | Planned | New layer ownership model | Schema only | Engine/UI/conflict tests |
| Cross-device chords | Planned | Explicit additive/consuming policy | Model tests planned | Visible consuming delay and HIL |
| Ordered/leader sequences | Planned | Stable ControlId histories, bounded/opt-in only | None | Privacy-safe capture and engine |
| Reference-counted shared held output | First-slice | Generalized Tippy ledger by execution owner | Shared-owner and release tests | Mouse/MIDI/gamepad expansion |
| Cleanup on unplug/lifecycle/emergency | First-slice | One owner-release path | Disconnect/profile/emergency tests | Physical lock/suspend/unplug HIL |
| Freeze key-down context through release | First-slice | New immutable execution lease | Mid-hold profile/layer tests | Application-scene coverage |
| Foreground application scenes | Planned | Resolve only on input, no polling | Interface/model tests planned | Full scene editor and Windows tests |
| Permission-gated application discovery | Planned | Local installed/Start Menu/visible process review | None | Implement; no documents/history/data |
| Windows/application catalogs | Planned | Adapt catalog organization, add scan-aware keys | None | Port data and search tests |
| MIDI/OSC setup and variable manager | Planned | Reusable named configuration | None | Implement and device tests |
| Rehearsal Mode | First-slice | Mapping path runs with output suppressed | Core/App tests plus current published/extracted-package smoke with zero injected input | Physical visual check |
| Emergency stop and output bounds | First-slice | Unique hotkey plus mouse/tray commands; depth/rate limits | Safety tests | Hotkey conflict/physical recovery test |
| Tray/background/startup | First-slice | Unique tray identity and recovery; startup deferred | App compiles; no automated tray interaction test | Tray/background manual test; startup settings UI |
| Themes and responsive modes | First-slice | Family tokens, code-rendered placeholder brand | App tests cover mode minimums, live automation names, and high-contrast theme precedence; WPF resources compile | Manual light/dark/high-contrast, DPI, and layout-mode accessibility checks |
| Backups/rollback/portable/recovery | First-slice | Atomic profile, LKG/quarantine; isolated smoke root | Store tests plus published/extracted profile round-trip smoke | Full UI and crash-recovery workflow |
| Updates without telemetry | Deferred | Future Tappy-only opt-in endpoint | None | Public release authorization |
| Immutable snapshots/migrations | First-slice | Schema v1 normalization and atomic replace | Round-trip/corrupt isolation tests | Version migrations beyond v1 |
| Live diagnostics/overlay | First-slice | Aggregate selected-device state with ordered bounded visual delivery | Redaction/state, quick-tap pulse, FIFO, and backlog-compaction tests | Overlay and bounded armed samples |
| Tappy Doctor | First-slice | Headless readiness runner implemented | Current clean post-provider published-directory and fresh-ZIP runs passed all four checks with zero injected inputs | Full interactive Doctor |
| Controller Passport/HIL | Hardware evidence required | General station plan plus finite, explicitly armed G13 input verifier | 23 verifier state-machine/refusal/evidence tests; no physical capture | Armed G13 input-functional record, full G13/K15 Passport and output/pass-through/latency HIL |
| Signed data-only controller packs | Planned | New extension/schema/trust store | Fixture only | Auth/sign/install/catalog implementation |
| Raw keyboard provider | First-slice | Scan/E0/E1/device identity, dedicated message thread, and ContainerId grouping | Normalizer/provider contracts plus authoritative K15 four-interface and Tartarus two-interface grouping | Physical K15/Targus/Tartarus make-break, rollover, pass-through, reconnect, and cleanup |
| Logitech G13 vendor-HID provider | Code-supported | Exact `046D:C21C`, `FF00:0000` provider; `C232` excluded; fixed 39-control model and stable tile grid | Decoder/provider/App tests plus 23 finite-verifier tests | No live button/stick capture yet; complete armed input run and broader Passport/HIL |
| Learned raw-HID provider | Planned | Core has a discrete provider seam with no fixed 32-control cap; current App composition is explicitly keyboard plus model-specific G13 | Interface and G13-specific composition only | Generic identity, selection/UI integration, learner, report schema, and real-device tests |
| MIDI/encoder/joystick triggers | Planned | Discrete Core seam; G13 has provider-specific raw X/Y and fixed-threshold directions, not a generic analog profile model | Interface plus model-specific G13 direction tests | Add generic analog values/threshold/deadzone schemas, integrate new providers, and hardware-test |
| Data-driven controller layouts | First-slice | Generic grid/registry plus a code-defined 39-control G13 model published in stable presentation order | Profile tests round-trip more than 100 controls; App tests cover G13 model/grid state; current package registry audit passed | Row/cluster-aware WPF projection, designer/templates, and reviewed art |
| Batch/drag/compare/learn-all/search | Planned | Tappy-specific mapping workflows | None | Implement |
| Source behavior/rollover/conflict pages | First-slice | Pass-through truth and live simultaneous state | State/repeat/simultaneous tests | Dedicated rollover/conflict screens and HIL |
| Usage heatmaps | Deferred | Local-only ControlId counts, disabled by default | None | Privacy review and opt-in UI |
| Final branding/artwork | Deferred | Placeholder only | Published/extracted payload audits exclude `PAD IMAGES` | Owner/license approval |
