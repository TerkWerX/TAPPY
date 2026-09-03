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
- Current deterministic results: Core 31, Windows 92, App 38, and G13 HIL tool 23
  tests pass (184 total) with a zero-warning, zero-error Release build.
- A locked restore, formatting verification, and all-project dependency advisory
  query pass; NuGet reports no known vulnerable packages in the current ten-project
  solution. This is an advisory checkpoint, not a complete security audit.
- A clean committed-source portable audit passes the three-file payload allowlist,
  all 184 tests, published and freshly extracted readiness smoke checks, ten package
  lock records, and zero injected input. The artifact remains an unsigned local
  checkpoint, not a public release.

### Known limitations

- No physical controller is called verified until real Controller Passport and HIL
  evidence is captured.
- Windows cannot selectively suppress one keyboard through Raw Input; original keys
  remain pass-through.
- The attached G13 is descriptor-enumerated and code-supported, but no live control
  capture or HIL run has completed; it is not Functional or Verified.
- Final mascot, wordmark, public license, signing, release, and website decisions are
  intentionally open.
- Processed controller images remain excluded pending provenance, usage rights,
  exact-model/protocol evidence, processing records, and explicit approval.
- Source/docs/CI publication to the existing public `TerkWerX/TAPPY` repository is
  authorized; a packaged release, signing, website, hosting, and final branding are
  not authorized or implied.
