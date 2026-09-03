# Changelog

Notable changes to Tappy are documented here.

## Unreleased

### Added

- Initial local `0.1.0` architecture and first-milestone implementation work.
- Honest Device-aware pass-through Raw Input design with separate requested and
  effective source modes.
- Privacy-first physical controller identity and support-output rules.
- Platform-neutral controller, control, layer, binding, layout, state, safety, and
  profile boundaries.
- A first Raw Input keyboard vertical slice, Rehearsal Mode, tagged SendInput output,
  held-output cleanup, emergency stop, profile persistence, and portable smoke audit.
- Deterministic Core, Windows, and App suites pass with zero Release-build warnings
  or errors; exact checkpoint counts and package results live in the testing record.
- A testing/evidence record that keeps automated results separate from unverified
  physical hardware, accessibility, latency, and packaged-artifact claims.
- Authoritative Windows ContainerId grouping: the attached Freewolf K15 now appears
  as one four-interface `1A2C:2D43` logical keyboard controller instead of four
  selectable interfaces.
- Descriptor-only inventory for the attached, user-identified Targus numberpad
  candidate (`05A4:9862`, one keyboard interface) and Windows-identified Razer
  Tartarus (`1532:0201`, two grouped keyboard interfaces), without promoting either
  to a functional support tier.
- A dedicated Logitech G13 input path for the exact physical `046D:C21C`,
  `FF00:0000` vendor-HID collection, with strict report validation, 39 code-defined
  controls, a stable code-rendered tile grid, provider-specific profile round-trip, and safe
  lifecycle/unplug/fault cleanup. The `046D:C232` G HUB virtual keyboard is excluded.
- Schema-3 descriptor inventory and an explicitly armed, finite G13 HIL verifier.
- A bounded ordered visual-transition buffer plus a truthful, minimum-duration
  illumination pulse so a quick make/break cannot disappear before WPF renders;
  overflow compaction preserves final physical states without delaying input/output.
- A synchronized pre-arm keyboard-neutrality guard prevents a held key's autorepeat
  from masquerading as the deliberate identification press.
- Cleanup dispatch results are now explicit. A rejected owned-output release latches
  a truthful Needs-attention state, forces Rehearsal Mode, and blocks re-arming until
  restart instead of claiming cleanup succeeded.
- A finite, explicitly armed focused-console Output Witness records aggregate-only
  evidence for Rehearsal suppression, normal F13-F24 output, source pass-through,
  and held-controller unplug cleanup without claiming device attribution.
- An attended first-milestone operator runbook binds that witness to one deliberately
  identified Targus numberpad while keeping K15, Tartarus, and G13 promotion separate.
- A privacy-bounded attended-record template keeps T01-T12, package/profile hashes,
  witness attribution, recovery, and closeout evidence explicitly pending until a
  human performs and reviews the physical run.
- Raw Input startup/shutdown is serialized and bounded, optional G13 registration
  failure no longer poisons keyboard capture, G13 interface membership changes disarm
  visibly, and disposal cannot race a stale input into new output.
- Cleanup failure now preserves the unclean-session marker, global-hotkey conflicts
  survive later status updates, and a refused Normal-mode request snaps the UI back
  to the effective Rehearsal state.
- Controller and output-key ComboBoxes now use explicit theme-aware item text instead
  of inheriting light app text onto the native white dropdown surface. Dark/light
  contrast and High Contrast system-color routing have deterministic regression tests.
- A shared button template keeps disabled actions visibly labeled instead of
  rendering a blank white bar. Controller tiles are wider/taller with smaller,
  wrapped labels, and the safety-critical Rehearsal label now wraps instead of clipping.
- Current deterministic results: Core 31, Windows 99, App 55, G13 HIL tool 23,
  and Output Witness 53 tests pass (261 total) with a zero-warning, zero-error
  Release build.
- A locked restore, formatting verification, and all-project dependency advisory
  query pass; NuGet reports no known vulnerable packages in the current twelve-project
  solution. This is an advisory checkpoint, not a complete security audit.
- A clean committed-source portable audit passes the three-file payload allowlist,
  all 261 tests, published and freshly extracted readiness smoke checks, twelve package
  lock records, and zero injected input. The artifact remains an unsigned local
  checkpoint, not a public release.
- A controller-native action-sequence profile schema and editor now combine up to
  500 ordered keyboard, Unicode text, delay, mouse, launch, PowerShell, MIDI, and OSC
  steps. Sequences support press, release, held-cleanup, and bounded repeat behavior.
- Windows MIDI output uses the local `winmm` short-message API with explicit device
  selection and strict note/CC/program validation. OSC output builds padded, typed
  packets and sends only to the host and UDP port entered in the assignment.
- The background action scheduler never blocks Raw Input, tags its keyboard/text/mouse
  injection, caps a sequence pass at 30 seconds and repeating output at 20 seconds,
  and joins emergency, unplug, lifecycle, profile-change, and fault cleanup through
  the same owned-output boundary.
- Current deterministic results: Core 46, Windows 103, App 62, G13 HIL tool 23,
  and Output Witness 53 tests pass (287 total).

### Known limitations

- No physical controller is called verified until real Controller Passport and HIL
  evidence is captured.
- Windows cannot selectively suppress one keyboard through Raw Input; original keys
  remain pass-through.
- The attached G13 is descriptor-enumerated and code-supported, with operator-reported
  visual response for all controls, but no finite HIL/output/pass-through run has
  completed; it is not Functional or Verified.
- Virtual gamepad, variables, layer-control actions, gesture triggers, reusable MIDI/
  OSC preset managers, and full independent press/release sequence editing remain.
- Final mascot, wordmark, public license, signing, release, and website decisions are
  intentionally open.
- Processed controller images remain excluded pending provenance, usage rights,
  exact-model/protocol evidence, processing records, and explicit approval.
- Source/docs/CI publication to the existing public `TerkWerX/TAPPY` repository is
  authorized; a packaged release, signing, website, hosting, and final branding are
  not authorized or implied.
