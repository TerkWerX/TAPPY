# Releasing Tappy

The owner has authorized publishing Tappy source, documentation, and CI configuration
to the public `https://github.com/TerkWerX/TAPPY` repository. Source publication does
not authorize a packaged software release, signing, website sync, or production-
hosting change.

Current build, test, and packaged-artifact checkpoint evidence is recorded in
[`TESTING.md`](TESTING.md). Exact package source revision, payload sizes, and hashes
come from the manifest generated with that artifact, not from this tracked document.

## Local release audit

`tools/Build-Portable.ps1` restores, builds, tests, publishes a self-contained
`win-x64` app to a newly created allowlisted staging directory, verifies every file
against an explicit payload policy, launches the published executable in harmless
readiness-smoke mode, and produces a portable ZIP plus a manifest of paths, sizes,
and SHA-256 hashes. The smoke run must load the packaged controller registry,
round-trip an isolated temporary profile, exercise the mapping engine in Rehearsal
Mode, and produce no injected input.

The staging audit rejects profiles, reports, logs, raw paths, secrets, undeclared
files, and reference artwork. Content loaded by file path must be marked for both
build and publish copying; embedded resources must be accessed through resource APIs.

## Future authorized release

A semantic-version tag workflow may, only after explicit authorization:

1. restore, build, and test the clean source tree;
2. publish self-contained `win-x64` into fresh staging;
3. run the packaged readiness audit;
4. discover the actual PE inventory and optionally Authenticode-sign every PE when
   signing secrets exist, then verify every signature;
5. create `Tappy-<version>-Portable-x64.zip` and the unique per-user Inno installer;
6. emit SHA-256 checksums and the package manifest from that same artifact; and
7. publish a GitHub release only from an authorized tag.

Unsigned builds must say so plainly and may trigger SmartScreen. The installer uses
a unique AppId, mutex, shortcuts, uninstall identity, and
`%LOCALAPPDATA%\Programs\Tappy`; it must coexist with Tippy without shared state,
startup entries, hotkeys, endpoints, or update behavior.

## Version/toolchain record

- Product target framework: .NET 8 (`net8.0`, `net8.0-windows`).
- Initial version: `0.1.0`.
- Tested SDK: recorded in `global.json` and the package manifest.
- Test dependencies: pinned in project files.
- Inno Setup: exact compiler version is recorded when an installer is actually built.

Website source and host-sync folders must never contain the full source tree. A
future website update uses a separately reviewed minimal `upload-ready` artifact and
release metadata generated from the one signed/versioned release artifact.
The configured `https://www.terkwerx.com/tappy/` value is the reserved Tappy identity
endpoint, not a claim of a live product page; it was not published and returned 404
in the 2026-09-02 audit.
