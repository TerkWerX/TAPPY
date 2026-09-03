## What changed

Describe the user-facing outcome and why this change is needed. Call out any change
to input selection, pass-through behavior, generated output, privacy, recovery, or
packaging.

## Verification

- [ ] `dotnet restore Tappy.slnx --locked-mode` succeeds
- [ ] `dotnet build Tappy.slnx -c Release --no-restore` succeeds with no warnings
- [ ] `dotnet test Tappy.slnx -c Release --no-build --no-restore` passes
- [ ] `pwsh -File tools/Build-Portable.ps1` passes from a fresh output location
- [ ] Affected interface paths were checked in dark and light themes
- [ ] Visible changes include screenshots with private data removed
- [ ] Hardware claims are backed by a reviewed Controller Passport/HIL record, or are explicitly marked unverified
- [ ] No profiles, mappings, typed text, key history, raw device paths, serial numbers, secrets, reports, or build output are committed

## Safety and privacy notes

Explain how unselected-device filtering, Rehearsal Mode, emergency stop, held-output
cleanup, and report redaction were preserved or tested. Write `Not applicable` only
when the change cannot affect those boundaries.

## Screenshots or hardware evidence

Attach privacy-reviewed screenshots or sanitized evidence when relevant. A simulator
or automated test is useful verification, but it is not physical-device evidence.
