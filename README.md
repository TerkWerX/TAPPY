# Tappy

[![Windows CI and portable audit](https://github.com/TerkWerX/TAPPY/actions/workflows/windows-ci.yml/badge.svg)](https://github.com/TerkWerX/TAPPY/actions/workflows/windows-ci.yml)

**Put every key to work.** *(provisional product line)*

Tappy is an early-development Windows 11 x64 utility for turning a deliberately
selected USB numpad or keyboard-style hand controller into a visual programmable
control surface. It is the hand-operated sister project to TerkWerX Tippy, with a
keyboard-native architecture rather than pedal concepts renamed.

## Current status

Version `0.1.0` is a public-source bootstrap, not a public packaged/binary release.
The source now implements the safe vertical slice for ContainerId-grouped Raw Input
keyboards plus a dedicated Logitech G13 vendor-HID provider. Both require deliberate
selection, press/release identification, neutral state, and explicit confirmation
before input can reach mapping. The UI shows press/release/repeat and simultaneous
state and maps one control to a harmless F13–F24 output in Rehearsal or normal mode.
The Release solution build passes with zero warnings or errors; 260 current
automated tests pass
(Core 31, Windows 99, App 54, G13 HIL tool 23, Output Witness 53). Exact package and
physical evidence boundaries are in [testing](docs/TESTING.md). The attended
[first-milestone operator run](docs/FIRST_MILESTONE_OPERATOR_RUN.md) defines the
finite Targus witness; its
[fillable evidence record](docs/FIRST_MILESTONE_RECORD_TEMPLATE.md) keeps every
physical check pending until observed. Broader K15, Tartarus, and G13 promotion is
separate.

Descriptor-only evidence now shows the attached K15 as one four-interface
`1A2C:2D43` keyboard group, the user-identified Targus numberpad candidate as one
`05A4:9862` keyboard interface, and the Razer Tartarus as one two-interface
`1532:0201` keyboard group. The attached G13 is one single-interface `046D:C21C`,
`FF00:0000` group with 39 code-defined controls in a stable code-rendered tile grid.
No key/button/stick capture, Controller Passport, or physical HIL run has completed
for these devices; all remain below Functional/Verified, and the G13 is
**code-supported only**. See the [G13 support boundary](docs/LOGITECH_G13.md).

The owner has authorized the source, documentation, and CI configuration for the
public `TerkWerX/TAPPY` repository. That source-publication decision does not
authorize a packaged software release, signing, website publication, production
hosting, or final branding. No public software license has been selected or granted;
all rights are reserved. Source visibility does not imply permission to use,
redistribute, or create derivative works from Tappy code or binaries. External
contributions should not be submitted or merged until the owner defines contribution
terms and adds an explicit license.

Tappy's initial source behavior is **Device-aware pass-through**. Windows Raw Input
can identify which physical source produced an event, but Tappy does not suppress
the source's ordinary Windows or vendor-software behavior. For keyboard-class
controllers, a mapped key may therefore run while the original key also reaches the
focused program. Tappy does not install a keyboard hook or filter driver and does
not claim exclusive per-device remapping.

## Safety and privacy

- Tappy never silently chooses the first controller. A controller must be selected,
  identified by a press-and-release check, and explicitly confirmed.
- Events from unselected controllers are discarded before control tracking, mapping,
  diagnostics, or UI publication.
- Ordinary diagnostics retain aggregate counts and current state, not typed text or
  chronological key histories. Raw device paths are never saved in profiles or
  support output.
- Generated `SendInput` events carry a Tappy-specific marker and are rejected by the
  input path when Windows preserves that marker. Device-less injected input is also
  rejected, while core ancestry, depth, and rate guards bound feedback behavior.
- Rehearsal Mode runs recognition and visual feedback without output.
- Emergency stop immediately attempts to release every output Tappy owns. If Windows
  rejects a release, Tappy reports that it cannot confirm a safe output state, forces
  Rehearsal Mode, and refuses re-arming until restart. Mouse-accessible window and
  notification-area commands remain available.
- SendInput is not firmware-level USB HID. Elevated, secure, exclusive-input, or
  anti-cheat-protected applications may reject it, and application/game rules win.

See [architecture](docs/ARCHITECTURE.md), [decisions](docs/DECISIONS.md),
[privacy and security](docs/PRIVACY_AND_SECURITY.md), and the
[parity matrix](docs/PARITY_MATRIX.md) for the implementation contract and honest
feature status. Exact automated, package, and physical evidence boundaries are in
[testing](docs/TESTING.md).

## Build locally

Requirements: Windows 11 x64 and an SDK capable of targeting .NET 8. The repository
records the tested SDK in `global.json` while all product projects target .NET 8.

```powershell
dotnet restore Tappy.slnx
dotnet build Tappy.slnx -c Release --no-restore
dotnet test Tappy.slnx -c Release --no-build
```

Create and audit a local portable build:

```powershell
pwsh -File tools/Build-Portable.ps1
```

That script publishes to a fresh allowlisted staging directory, checks the declared
payload, launches the published and extracted `Tappy.exe` in readiness-smoke mode,
and writes a SHA-256 manifest. The generated manifest, rather than tracked prose, is
the authority for the exact source revision, payload sizes, and hashes of each local
artifact. The script does not publish a release, push source, or change a website.

## Repository layout

```text
src/Tappy.Core/          Platform-neutral input, profiles, layers, safety, layouts
src/Tappy.Windows/       Raw Input keyboard/G13 providers, SendInput, storage, lifecycle
src/Tappy.App/           WPF interface and composition root
tests/                   Deterministic core, Windows, app, and finite-witness tests
tools/                   Device probe, focused output/G13 witnesses, pack signer, portable audit
controller-packs/        Data-only layout registry and trust metadata
docs/                    Architecture, decisions, security, evidence, release notes
installer/               Unique per-user Inno Setup definition
```

The raw and processed files under `PAD IMAGES` are separately managed reference
artwork. They are preserved but not shipped or treated as hardware/protocol evidence
until source rights, exact models, and reviewer approval are recorded.

## Non-goals for this milestone

Global blocking, per-device exclusive input, generic learned raw-HID support beyond
the dedicated G13 provider, arbitrary analog mappings, MIDI/joystick providers, G13
LCD/lighting output, full macro parity, controller support packs, polished brand
artwork, and public packaged/binary distribution remain future work. Their extension
boundaries are documented; the UI and README do not advertise them as complete.
