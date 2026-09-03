# Contributing to Tappy

Thanks for helping Tappy turn deliberately selected USB numpads and keyboard-style
hand controllers into useful control surfaces. Keep changes focused, test the safety
boundary they touch, and describe user-visible behavior plainly.

No public license or contribution terms have been selected yet. All rights are
reserved; please do not submit external contributions, and maintainers must not
merge them, until those terms are published.

## Development setup

The tested baseline is Windows 11 x64. Product projects target .NET 8; install the
SDK selected by `global.json` so local and CI builds use the same toolchain.

```powershell
dotnet restore Tappy.slnx --locked-mode
dotnet build Tappy.slnx -c Release --no-restore
dotnet test Tappy.slnx -c Release --no-build --no-restore
```

Package changes must also pass the local portable audit from a fresh output
location:

```powershell
pwsh -File tools/Build-Portable.ps1
```

The audit performs a locked restore, Release build and test, self-contained Windows
publish, strict payload check, harmless readiness smoke test, fresh-ZIP extraction
test, and SHA-256 manifest generation. It deliberately refuses to overwrite an
existing package. It does not create a release or publish anything.

Commit updated `packages.lock.json` files when dependencies intentionally change.
Do not weaken locked restore, warning-as-error, payload allowlists, or smoke checks
to make a pull request pass.

## Input and output contract

Tappy's current source mode is **Device-aware pass-through**. Raw Input identifies
the physical source, but the selected controller's original keystroke may still
reach the focused application. Never describe this as exclusive capture,
suppression, or per-device remapping. A future suppression backend requires a
separate reviewed design; do not add hooks or drivers as an incidental change.

Preserve these invariants:

- no keyboard is selected automatically;
- selection requires identify, release-to-neutral, and explicit confirmation;
- unselected-device events are discarded before tracking, diagnostics, or UI;
- Rehearsal Mode performs recognition and visualization with zero generated output;
- Tappy-generated input is marked and recursion/rate bounded;
- unplug, lock, suspend, backend failure, profile swap, emergency stop, and exit
  release every output Tappy owns; and
- UI state is event-driven and coalesced rather than polled.

Use harmless F13–F24 mappings for development whenever practical. Do not test input
generation against elevated, secure, anti-cheat, or production-sensitive software.

## Device-data privacy

Keyboard telemetry can reconstruct private text even when it is represented as scan
codes. Never commit or paste:

- typed text or chronological key histories;
- raw or reversible Windows device paths and serial numbers;
- real profiles, mappings, macros, clipboard content, arguments, or scripts;
- usernames, computer names, documents, screenshots with private content, tokens,
  credentials, private keys, or environment secrets; or
- support reports, crash dumps, logs, build outputs, or locally generated packages.

Fixtures must be minimal, synthetic, non-chronological where possible, and labeled
as synthetic. Persistent identity uses sanitized metadata and one-way fingerprints;
do not add raw paths to schemas, logs, exceptions, or issue instructions. Tappy does
not translate captured events into typed characters and does not upload reports.

## Controller and hardware evidence

Code review and automated tests can verify parsing and state machines, but they do
not prove a physical controller works. Hardware support claims require a reviewed
Controller Passport and, where output behavior is claimed, the HIL procedure in
`docs/HARDWARE_TEST_STATION.md`.

Record observed make/break, repeat, rollover, simultaneous controls, reconnect, and
unplug-while-held behavior using only the controller under test. Store sanitized
evidence in the documented `.tappy-passport.json` and `.tappy-hil.json` formats.
Until that evidence exists, describe the device as observed or unverified and leave
`docs/COMPATIBILITY.md` honest. Artwork, shared VID/PID, appearance, and marketing
names are not protocol evidence.

`Tappy.DeviceProbe` provides descriptor-only inventory and never registers for or
captures input:

```powershell
dotnet run --project tools/Tappy.DeviceProbe/Tappy.DeviceProbe.csproj -c Release -- --json
```

Review its output before sharing and include only the relevant sanitized entry.

## Pull requests

Before opening a pull request:

- search existing issues and keep the change scoped;
- run the locked Release build, complete test suite, and portable audit;
- exercise affected UI paths in dark and light themes;
- include redacted screenshots for visible changes;
- add tests for input identity, simultaneous state, held-output cleanup, and privacy
  boundaries when relevant;
- explain changes to permissions, native APIs, package contents, or external
  dependencies; and
- label simulator-only or automated-only device results as non-physical evidence.

Security vulnerabilities belong in a private GitHub Security Advisory, not a public
issue. See `SECURITY.md`.
