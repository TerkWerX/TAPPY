# First-milestone attended evidence record

> Template status: unperformed. Every physical check begins as `Pending`.
>
> Copy this file into an opaque directory beneath `artifacts/hil/` for the
> attended run. Do not edit this tracked template into a result and do not commit
> a filled record without a separate privacy review and explicit owner decision.

Use this record only with
[`FIRST_MILESTONE_OPERATOR_RUN.md`](FIRST_MILESTONE_OPERATOR_RUN.md). Expected
behavior is not evidence. Replace placeholders only with observations from the
same attributable device, application instance, and clean package revision.

Allowed check states are `Pending`, `Pass`, `Fail`, and `Blocked`. Stop the run at
the first unsafe or ambiguous result; retain the exact failing observation without
typed content or raw device paths.

## Session and package identity

| Field | Observed value |
|---|---|
| Opaque run ID | `<pending>` |
| UTC start | `<pending>` |
| UTC end | `<pending>` |
| Operator initials | `<pending>` |
| Local physical console, not remote | `Pending` |
| Windows version/build | `<pending>` |
| Num Lock at start | `<pending>` |
| Source commit (full SHA) | `<pending>` |
| Source state was clean | `Pending` |
| Portable manifest path | `<pending>` |
| Portable manifest SHA-256 | `<pending>` |
| Manifest `source.commit` matched source commit | `Pending` |
| Manifest `source.dirty` was `false` | `Pending` |
| Portable ZIP path | `<pending>` |
| Portable ZIP SHA-256 | `<pending>` |
| ZIP hash matched manifest | `Pending` |
| Fresh extraction directory | `<pending>` |
| Extracted `Tappy.exe` SHA-256 | `<pending>` |
| Executable hash matched manifest payload | `Pending` |
| Tappy version | `<pending>` |

## Selected physical controller

Do not paste a Raw Input path or ContainerId. The Device Probe fingerprint is
already a sanitized SHA-256 identity and may be recorded.

| Field | Observed value |
|---|---|
| Operator device label | `Targus PAUK10U candidate` |
| Tappy picker label | `<pending>` |
| VID:PID | `<pending>` |
| Usage page:usage | `<pending>` |
| Sanitized Device Probe fingerprint | `<pending>` |
| Grouping | `<pending>` |
| Interface count | `<pending>` |
| Physical source control | `Numpad 1 / End` unless the runbook requires stopping for a discrepancy |
| Tappy output control | `F24` |
| Num Lock variants observed | `<pending>` |
| Connection/reconnect observation | `<pending>` |
| Vendor software/profile, if any | `<pending or not installed>` |

## T01–T12 result ledger

All rows must refer to the detailed numbered procedure in the operator runbook.
Record concise state/count evidence, not the characters produced by general typing.

| ID | Required observation | State | Evidence or concise observation |
|---|---|---|---|
| T01 | Safe startup, unavoidable pass-through notice, deliberate target selection, other-device rejection, make/break identification, explicit confirmation | `Pending` | `<pending>` |
| T02 | Distinct physical controls produce accurate make/break visual state and return to `Down: None` | `Pending` | `<pending>` |
| T03 | Repeat, simultaneous state, rapid alternation, and Num Lock variants behave accurately | `Pending` | `<pending>` |
| T04 | Harmless `Numpad 1 / End` to `F24` mapping is assigned and saved with the intended controller/layer | `Pending` | `<pending>` |
| T05 | Rehearsal recognizes source make/break while the focused witness observes zero F24 transitions through its quiet window | `Pending` | `<pending>` |
| T06 | Normal mode produces one balanced F24 cycle, observes source repeat, and does not recurse or flood | `Pending` | `<pending>` |
| T07 | Original Numpad 1 pass-through reaches blank Notepad and the limitation remains labeled accurately | `Pending` | `<pending>` |
| T08 | Restart reloads controller identity, layout, layer, and binding but remains in Rehearsal and does not auto-arm | `Pending` | `<pending>` |
| T09 | Global emergency chord releases output; mouse and tray recovery paths remain independently reachable | `Pending` | `<pending>` |
| T10 | Unplug while the mapped source remains physically held produces a balanced synthetic F24 release with no observed source break | `Pending` | `<pending>` |
| T11 | Reconnect requires deliberate re-identification and input from other attached controllers remains isolated | `Pending` | `<pending>` |
| T12 | Rehearsal restored, `Down: None`, Emergency stop invoked, orderly tray exit, hashes/privacy reviewed, narrow verdict recorded | `Pending` | `<pending>` |

## Aggregate UI observations

| Observation | Recorded value |
|---|---|
| Tappy aggregate count before T01 | `<pending>` |
| Tappy aggregate count after T11 | `<pending>` |
| Maximum simultaneous count actually observed | `<pending>` |
| Final last transition | `<pending>` |
| Final `Down` summary | `<pending>` |
| Final Tappy status | `<pending>` |
| Pass-through/source label observed | `<pending>` |
| Rehearsal status observed | `<pending>` |
| Normal-output status observed | `<pending>` |

## Focused Output Witness evidence

The witness has `deviceSourceAttribution: none`. A row is relevant only when the
operator record also establishes that the intended Targus was confirmed and the
harmless witness console retained focus for that scenario.

| Scenario/check | JSON path | SHA-256 | Outcome | Key assertions/counts reviewed | Console focus and physical source attested |
|---|---|---|---|---|---|
| `rehearsal` / T05 | `<pending>` | `<pending>` | `Pending` | `<pending>` | `Pending` |
| `basic` / T06 | `<pending>` | `<pending>` | `Pending` | `<pending>` | `Pending` |
| `held-unplug` / T10 | `<pending>` | `<pending>` | `Pending` | `<pending>` | `Pending` |

## Saved profile evidence

Never paste a macro body, typed text, user name, or absolute user profile path.

| Field | Recorded value |
|---|---|
| Symbolic path | `%LOCALAPPDATA%\Tappy\default.tappy.json` |
| SHA-256 before restart | `<pending>` |
| SHA-256 after unchanged reload | `<pending>` |
| Schema/version | `<pending>` |
| Controller identity/provider | `<pending>` |
| Layout ID/control count | `<pending>` |
| Active layer | `<pending>` |
| Sanitized binding summary | `Numpad 1 / End -> F24` or `<pending discrepancy>` |
| Reload preserved all required fields | `Pending` |
| Restart remained Rehearsal and unarmed | `Pending` |

## Recovery and final-state attestation

| Requirement | State | Observation |
|---|---|---|
| Global emergency chord was available and worked | `Pending` | `<pending>` |
| Mouse-accessible Emergency stop was available and worked | `Pending` | `<pending>` |
| Tray recovery/exit was available and worked | `Pending` | `<pending>` |
| No Tappy-owned output remained held | `Pending` | `<pending>` |
| Final Tappy visual state was `Down: None` | `Pending` | `<pending>` |
| Primary keyboard and mouse remained usable | `Pending` | `<pending>` |

## Failures, blocks, and limitations

First failing check: `<none recorded; pending>`

Concise observation and safe recovery performed: `<pending>`

Remaining limitations relevant to the narrow milestone decision: `<pending>`

Do not use this Targus record to promote the K15, Tartarus, G13, another Targus
unit, or the complete Targus product family. Record those in separate Passport/HIL
runs under the boundaries in the operator runbook and hardware-test document.

## Privacy closeout

Mark each row only after reviewing the final bundle itself.

| Bundle review | State |
|---|---|
| No ordinary typed content or chronological general-key history | `Pending` |
| No raw device path or ContainerId | `Pending` |
| No user/computer name or unsanitized absolute profile path | `Pending` |
| No macro/text body, clipboard content, arguments, PowerShell, or secret | `Pending` |
| No unrelated application/window/document content | `Pending` |
| Every referenced evidence file has a recorded SHA-256 | `Pending` |
| Failed/aborted evidence was retained honestly and not edited into a pass | `Pending` |

## Narrow first-milestone verdict

Verdict: `Pending`

The only allowed passing decision is `Pass` after T01–T11 all pass on the same
attributable controller and exact clean package, T12 closeout passes, every output
witness assertion is reviewed, final output is released, and no observation
contradicts the first-milestone definition of done. Otherwise record `Fail` or
`Blocked` and identify the first failing check.

Operator initials: `<pending>`

UTC decision time: `<pending>`
