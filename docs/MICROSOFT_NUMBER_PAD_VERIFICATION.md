# Microsoft X821504-001 attended verification

## Scope and baseline — 2026-09-18

The owner reported that the corrected physical layout, photo, and button feedback
look good and work. This is operator-reported basic presentation/input evidence,
not a completed Num Lock matrix, output witness, or certification result.

At 18:41 UTC, read-only inspection confirmed that TESTBED was running
`C:\TappyLab\20260918-microsoft-keypad\app\Tappy.exe` with SHA-256
`908E77177221BB1472A443A9629EFD5ABBEFAEA2B4BDEDC3093E5B66D0F40DBF`.
The saved Microsoft profile contains `microsoft-bluetooth-number-pad-layout-v1`,
17 physical-key controls, and zero bindings in each of its three layers.
The running application's live Rehearsal state was not read over SSH; the owner
must verify the checked box before each rehearsal step.

Profile/configuration backup (kept on TESTBED, not copied into source control):
`C:\TappyLab\20260918-microsoft-keypad\verification-baseline-20260918-144154`.
The backed-up `default.tappy.json` SHA-256 is
`9CCF18DAC0A09962FC91C9A6F5D22F00B5267076C7831C9AD7E290CBC5619ECF`.
No app was restarted, no assignment changed, and no output/capture tool armed
while establishing this baseline.

## Checks

| Check | State | Evidence / next action |
|---|---|---|
| Correct layout and basic photo/key highlighting | Operator-reported pass | Owner: “Looks good and everything works,” with screenshot showing 17 ordered keys and matching Num Lock highlight. |
| Both Num Lock states | Difference reported; investigating | Owner reports all keys still respond in Tappy, but native output changes from numbers to navigation when Num Lock is off. Separate numeric/navigation assignments have not yet been established. |
| Assignment save/restart/edit | Pending | Use a deliberately harmless test binding; save, exit via tray, reopen, confirm, and check its editor contents. Preserve layout and original mappings. |
| Normal output and release | Pending | Use a reviewed F13–F24 binding and the focused, explicitly armed Output Witness. Rehearsal must be intentionally disabled by the operator. No ordinary application, shell, or game may be the output target. |
| Bluetooth idle/off/on recovery | Pending | Observe presentation and exact-device recovery without re-pairing. Distinguish inactivity from a Windows device-removal notification; record actual behavior. |
| Windows sleep/resume | Pending | Observe retained controller/assignments and intended output mode, with no stale held output. |

## First check — Num Lock

1. Leave Rehearsal Mode checked. Use the Microsoft pad attached to TESTBED, not
   the typing keyboard or remote computer's keypad.
2. Open a blank disposable Notepad document beside Tappy and click inside the
   document. Watch Tappy while the harmless native pass-through keys go to Notepad.
   Rehearsal suppresses mapped output, not native keyboard input.
3. Press and release one key at a time in physical order:
   `/ * -`, `7 8 9 +`, `4 5 6`, `1 2 3 Enter`, `0 Decimal`.
4. Press/release the pad's Num Lock once. Repeat the same sweep. An LED is not
   required to identify the two passes; native number-versus-navigation behavior
   can help distinguish them. Do not assume the photographed battery indicator
   is a Num Lock indicator.
5. Observe 5, 0, Decimal, Enter especially. Record any newly added tiles rather
   than rearranging or deleting them. After every release, the pressed/down state
   must clear; the selected tile/photo outline may remain selected normally.
6. Click Save profile using the mouse and report both passes. No trace is needed
   for an unambiguous successful result; if a discrepancy occurs, investigate
   narrowly with the owner's participation.

Remaining steps will be guided individually. A timeout, ambiguous observation,
or interrupted test remains pending/failed, not a pass. Driver suppression,
signing, HLK, and anti-cheat review are outside these checks.

## Num Lock follow-up

The generic keyboard normalizer intentionally builds the existing physical control
ID from scan code and E0/E1, using VK only when the scan code is absent. The saved
Microsoft model has ordinary scan-based IDs. The owner's observation is consistent
with a VK-only difference between numeric/navigation modes, but that distinction
is not yet a captured hardware result. Do not assume Targus E0 banking applies.

At the owner's request, `tools/Tappy.KeypadCapture` provides two finite,
mouse-started, target-only aggregate captures. Read its README for limits and
privacy boundaries. No application mapping or identity changes were made while
preparing the diagnostic. Capture and review the actual VK/scan/extended fields
before deciding how to expose alternate assignments and preserve held-key releases.

Diagnostic staged (not launched) at
`C:\TappyLab\20260918-microsoft-numlock-capture\Tappy.KeypadCapture.exe`;
desktop shortcut **Tappy - Microsoft Num Lock Capture**. The shortcut targets only
the previously verified Microsoft fingerprint. SHA-256 checked after transfer:
`F83CDE44E8D788AC0C60427757041D78E933240DFF9C3D89F1B37BA59184E6F5`.
Unique aggregate reports will be written into that staging folder's `reports`
subdirectory. All 568 solution tests passed, including 11 capture-session cases;
the unshown diagnostic window was rendered and inspected without arming input.
No physical two-pass capture result exists yet. Tappy's running EXE and profile
were left unchanged; the saved Microsoft layout still contains 17 controls.

### Diagnostic revision 2 — premature abort

The owner reported an unusable/grayed Finish control. Read-only inspection of
the saved diagnostic summaries found two attempts marked **Aborted / Target
connection changed**, each with one pass and zero accepted/discarded packets;
another window was closed without starting a pass. These are not successful
captures and remain preserved.

The original tool aborted on any notification referring to the target, including
registration-time arrivals that the provider labels MembershipChanged even when
the member set is unchanged. Revision 2 compares exact persistent/session/model
identity and actual member handles before aborting; genuine removal still stops
capture. It also keeps Finish enabled during an active pass and makes abort status
unambiguous. A provider-level regression replays the delayed arrival after arming,
completes both number/navigation passes, and separately verifies real removal
still aborts. Hardware retry remains pending; no mappings in Tappy were changed.

Revision 2 was staged under the diagnostic folder's `v2` subdirectory and the
same desktop shortcut was updated only after validating its existing target and
backing up the shortcut. Original binaries/reports remain intact. Verified EXE
SHA-256: `DD3DDF24C1AAC15CC885D0896F03ADF7CC38B2DBB81BDE3DFCA3C1569783326B`.
The title explicitly says **diagnostic v2**. All 575 solution tests passed,
including 18 diagnostic-session/connection cases. The tool was not launched or
armed automatically. The owner should retry the two passes with Rehearsal checked.
