# First-milestone physical operator run

> Procedure status: ready for an attended run. No physical step in this document
> is recorded as passed. Descriptor inventory and automated tests are not substitutes
> for the operator observations below.

This is the finite manual procedure for the first Tappy milestone. It deliberately
separates two evidence scopes:

1. The **binding milestone witness** uses one deliberately selected Targus numberpad
   through the complete narrow vertical slice required by the kickoff definition of
   done.
2. **Compatibility-promotion work** for the Freewolf K15, Razer Tartarus, and
   Logitech G13 is broader. It is useful evidence, but a full Passport/HIL promotion
   is not silently made a prerequisite for the one-numpad first milestone.

The first scope comes from the one-spare-numpad definition of done in
[`_PROMPT.md`](../_PROMPT.md) and
[`PROJECT_TEMPLATE.md`](PROJECT_TEMPLATE.md). The wider per-control, rollover,
lifecycle, latency, and multi-controller matrix remains governed by
[`HARDWARE_TEST_STATION.md`](HARDWARE_TEST_STATION.md). A G13 verifier pass is only
input-functional evidence and claims no compatibility tier.

## Known attached identities

Re-run `Tappy.DeviceProbe` at the start of the attended session. The expected
descriptor associations are:

| Operator identification | Picker label | VID:PID | Current provider boundary |
|---|---|---|---|
| Targus PAUK10U candidate | `Keyboard VID 05A4 PID 9862` | `05A4:9862` | One keyboard interface |
| Freewolf K15 candidate | `Keyboard VID 1A2C PID 2D43` | `1A2C:2D43` | Four ContainerId-grouped keyboard interfaces |
| Razer Tartarus | `Keyboard VID 1532 PID 0201` | `1532:0201` | Two ContainerId-grouped keyboard interfaces; other collections are outside the generic provider |
| Logitech G13 | `Logitech G13` | `046D:C21C` | Exact `FF00:0000` vendor-HID collection |

These associations are descriptor evidence only until a human performs the targeted
press/release identification. Record the fresh sanitized fingerprint, grouping, and
interface count from the run; do not copy a stale fingerprint from prose.

## Safety and stop rules

- Perform every armed step locally and attended. Never leave either verifier armed
  while the operator is asleep or away from the machine.
- Keep a primary keyboard and mouse connected and unselected. Never select the
  primary keyboard merely to make a test easier.
- Release every key/control on every keyboard before clicking **Identify this
  device**. The keyboard provider's pre-arm neutral check is intentionally global.
- Keep Rehearsal Mode on until the runbook explicitly says to enable normal output.
  Rehearsal suppresses Tappy output, not the controller's original pass-through
  behavior.
- Use only a blank, disposable local target. Do not focus a shell with a pending
  command, a document containing real data, a password field, a game, an elevated
  program, or anti-cheat/exclusive-input software.
- After confirmation, keep the blank target or Output Witness console focused, not
  a Tappy button. Pass-through Enter or Space can activate a focused WPF control.
- Use only F13-F24 for Tappy output. The target and Tappy must run at the same
  integrity level because Windows may reject `SendInput` across integrity levels.
- For unplug-while-held, use an accessible device cable or a suitable USB hub. Do
  not strain the computer's connector. Keep the test key physically down until the
  Output Witness instructs otherwise, unplug, and only then release the disconnected
  key.
- Before sweeping gaming-pad controls, disable or replace vendor macros with a
  reviewed harmless test profile. Device-local, media, mouse, and vendor functions
  can still act because Tappy is pass-through.
- Ctrl+C aborts either console verifier. A timeout, provider fault, lifecycle event,
  or disconnect is not a pass. Preserve failed/aborted evidence and start a new run;
  do not edit evidence into a passing record.
- Use the tray **Exit Tappy** command for an orderly restart. Closing the window or
  minimizing it intentionally hides Tappy without exiting.
- Do not build, run, write, or redirect any command in `F:\TIPPY`. This procedure is
  confined to `F:\TAPPY`, the selected package extraction, and Tappy's own local
  application-data directory.

Stop immediately and restore Rehearsal Mode if output remains held, the status says
**Needs attention**, a selected device cannot be identified unambiguously, or the
observed original action is not harmless. Use the window or tray Emergency stop if
the global chord is unavailable.

## Evidence bundle

Create one local run directory outside the portable payload. Give it an opaque run
ID, copy
[`FIRST_MILESTONE_RECORD_TEMPLATE.md`](FIRST_MILESTONE_RECORD_TEMPLATE.md) into it,
and fill that copy during the attended session. Keep the tracked template unchanged.
Record these fields before touching a controller:

- UTC start/end, operator initials, Windows version/build, local-console versus
  remote session, and whether Num Lock was on;
- source revision, clean/dirty state, package-manifest SHA-256, archive SHA-256,
  tested `Tappy.exe` SHA-256, and Tappy version;
- device's operator label, picker label, VID/PID, usage, fresh sanitized fingerprint,
  grouping, interface count, connection/reconnect observation, and vendor-software
  profile/version when relevant;
- each check ID below, exact expected result, concise observed result, `Pending`,
  `Pass`, `Fail`, or `Blocked`, and any screenshot/video filename plus SHA-256;
- Tappy aggregate event count before/after, last transition, maximum simultaneous
  count actually observed, final **Down** summary, and final Tappy status;
- Output Witness evidence path/hash/outcome/assertions/counts for each verifier run;
- profile path written symbolically as
  `%LOCALAPPDATA%\Tappy\default.tappy.json`, profile SHA-256, schema, controller
  identity/provider, layout ID/control count, active layer, and binding; and
- a final privacy review confirming that the retained bundle contains no typed
  content, chronological general-key history, raw device path, user/computer name,
  profile text action, secret, or unrelated application/window content.

The manual record is the attribution layer. `Tappy.OutputWitness` intentionally has
`deviceSourceAttribution: none`; its result becomes relevant only when the record
also shows that Tappy had explicitly confirmed the intended physical controller and
that the focused console was the harmless foreground target.

Every check starts as `Pending`. Do not pre-fill a pass from expected behavior,
automated tests, device presence, or a prior run.

## Unarmed preflight

1. Ensure no Tappy instance is hidden in the notification area. Exit it from the
   tray if necessary.
2. Preserve any existing `%LOCALAPPDATA%\Tappy` data. Use a dedicated Windows test
   account or make a reviewed backup; never silently delete an existing profile.
3. Select the newest audited portable manifest whose `source.dirty` is `false` and
   whose source commit matches the revision under test. Verify its archive and
   payload hashes, extract the ZIP to a fresh directory, and record the extracted
   `Tappy.exe` hash. Physical observations made with a different binary do not prove
   the packaged binary.
4. From `F:\TAPPY`, restore/build the same revision and run only the unarmed probes:

```powershell
dotnet restore Tappy.slnx --locked-mode
dotnet build Tappy.slnx -c Release --no-restore
dotnet run --project tools/Tappy.DeviceProbe/Tappy.DeviceProbe.csproj -c Release --no-build -- --json
dotnet run --project tools/Tappy.OutputWitness/Tappy.OutputWitness.csproj -c Release --no-build -- --help
dotnet run --project tools/Tappy.G13Hil/Tappy.G13Hil.csproj -c Release --no-build -- --help
```

5. Confirm that the descriptor inventory contains the four expected entries above.
   The probe never registers input or captures controls.
6. Open a new blank Notepad document for explicit text pass-through checks. Do not
   save its contents. Keep the Output Witness terminal separate.

## Part A — binding Targus milestone witness

Use `Numpad 1 / End` as the source and F24 as the output unless the physical unit
does not emit that source identity. If it differs, stop and document the discrepancy
before choosing another `NumPad0`-`NumPad9` key supported by Output Witness. Set Num
Lock on before every Output Witness scenario, as required by its focused-console
contract; restore the operator's original lock state at closeout.

### T01 — safe startup and deliberate selection

1. Launch the freshly extracted `Tappy.exe`.
2. Dismiss the splash with the mouse. Confirm that Rehearsal Mode is checked, no
   controller is confirmed, and the device list says none was selected
   automatically.
3. Record the unavoidable **PASS-THROUGH** notice and
   `Effective: Pass-through` label.
4. In the picker select `Keyboard VID 05A4 PID 9862`.
5. With every controller neutral, click **Identify this device** using the mouse.
6. Press/release one harmless key on another attached controller. The Targus
   identification status must not advance.
7. Press and release physical Targus `1 / End` exactly once. The status must progress
   from detected press, through release, to **Click Confirm selected controller**.
8. Click **Confirm selected controller** using the mouse.

Acceptance: confirmation was impossible before a complete target press/release;
another device could not satisfy the handshake; the active label is the selected
VID/PID; status says only its events reach mappings; pass-through remains explicit.

### T02 — make, break, distinct controls, and visual state

Keep Rehearsal Mode on and the blank target focused. Press and release each physical
shell control twice, one at a time:

`Num Lock`, `/`, `*`, `Backspace`, `7/Home`, `8/Up`, `9/PgUp`, `-`, `4/Left`, `5`,
`6/Right`, `+`, `1/End`, `2/Down`, `3/PgDn`, `Enter`, `0/Ins`, `000`, and `./Del`.

For every emitted normalized control, record the physical label, Tappy tile label,
event-count delta, press illumination/state, release state, and final **Down: None**.
An ordinary two-cycle control should normally add four aggregate transitions.
`000` may be firmware-defined as repeated Numpad 0 rather than a uniquely
addressable control; record its actual behavior and do not claim it as an independent
Tappy control unless the emitted identity proves that.

Acceptance: every claimed control has balanced physical make/break evidence; quick
taps remain visibly illuminated long enough to see; Numpad Enter is distinct from
ordinary Enter; missing, duplicate, or device-local-only shell controls are listed
as limitations rather than inferred.

### T03 — repeat, simultaneous state, rapid alternation, and Num Lock

1. Hold Targus `1 / End` until the event summary visibly reports `last: repeat`, then
   release. Record that the tile remained pressed during repeat and ended released.
2. Hold `1 / End` and `3 / PgDn` together. While both are physically down, record
   two illuminated tiles, both names in **Down**, and `simultaneous: 2`; then release
   both and record **Down: None**.
3. Repeat with `4 / Left`, `5`, and `6 / Right`, expecting a simultaneous count of
   three if the hardware reports it. Record actual hardware behavior, never an
   inferred rollover value.
4. Rapidly alternate `1 / End` and `3 / PgDn` for ten complete cycles each. Record
   balanced end state and the aggregate-count delta.
5. With Num Lock on, press `1 / End` once in blank Notepad and record the harmless
   original `1`. Turn Num Lock off, establish a harmless caret position, press the
   same physical key once, and record navigation behavior. Tappy must illuminate the
   same physical-control tile in both lock states. Restore the operator's original
   Num Lock state afterward.

Acceptance: repeat is classified without inventing taps, simultaneous state is
truthful, rapid alternation ends balanced, and lock state changes original Windows
meaning without changing Tappy's scan-based physical identity.

### T04 — assign and save the harmless mapping

1. Press/release `1 / End` once so its tile is selected.
2. Click **Choose keyboard assignment…**, search for `F24`, select the direct
   **F24 key** entry, choose **Hold until controller key is released**, and click
   **Use selected assignment**.
3. Confirm the tile says `Hold F24 until release` and Rehearsal Mode still says it
   suppresses output.
4. Click **Save profile** with all controls released. Record the symbolic path and
   hash the resulting profile; do not retain a screenshot containing an absolute
   user-profile path.

### T05 — Rehearsal Mode output suppression

Confirm Rehearsal Mode is checked, then run this in the dedicated console:

```powershell
dotnet run --project tools/Tappy.OutputWitness/Tappy.OutputWitness.csproj -c Release --no-build -- --arm --ack-focused-console-only --ack-no-device-attribution --ack-tappy-mode-set --scenario rehearsal --original-key NumPad1 --output-key F24 --timeout-seconds 120
```

Leave that console focused. Press/release Targus Numpad 1 exactly once and then touch
nothing during its fixed two-second quiet window.

Acceptance: exit code 0; evidence outcome `Passed`; one original down/up; zero F24
down/up; quiet window completed; neither key ends held; all assertions are true.
This proves focused-target pass-through plus no selected output, not device identity
by itself.

### T06 — normal output, held repeat, and bounded self-injection

Uncheck Rehearsal Mode with the mouse. Confirm the mapping status says normal output
is enabled for the confirmed controller, then run:

```powershell
dotnet run --project tools/Tappy.OutputWitness/Tappy.OutputWitness.csproj -c Release --no-build -- --arm --ack-focused-console-only --ack-no-device-attribution --ack-tappy-mode-set --scenario basic --original-key NumPad1 --output-key F24 --timeout-seconds 120
```

Leave the console focused. Press and hold Targus Numpad 1 until the source repeats,
then release it once.

Acceptance: exit code 0; one original physical down, at least one original repeat,
one original up, exactly one F24 down/up, no duplicate/unbalanced F24 transition,
and no final held F24. Tappy must show only the selected physical source's events;
no F24 tile or recursive aggregate-event burst may appear. The aggregate witness
and deterministic injection-marker tests together support the no-recursion claim.

### T07 — explicit original-input pass-through

With normal output still enabled and blank Notepad focused, press/release Targus
Numpad 1 once. Record that the original `1` reaches Notepad while Tappy illuminates
the source and dispatches the mapping. F24 is intentionally harmless and normally
has no visible Notepad action. Do not claim suppression: the banner must still say
pass-through.

### T08 — profile reload without auto-arming

1. With everything released, save again and record the profile hash.
2. Exit through the tray **Exit Tappy** command; the window close button is not an
   exit command.
3. Restart the same extracted `Tappy.exe`.
4. Record that the saved profile loaded but no controller was automatically armed,
   and Rehearsal Mode is restored.
5. Select the same Targus picker entry and repeat the mouse-driven identify,
   press/release, and confirm sequence.
6. Confirm that the restored tile retains `Hold F24 until release`, active layer is
   `Layer 1`, and source mode is still effective pass-through.
7. Inspect the saved JSON locally. Record the same persistent controller identity,
   provider, VID/PID/usage, layout/control ID, three available layers, active
   `layer-1`, and the F24 `HoldUntilRelease` binding. Do not paste the complete
   profile into a support record.
8. Re-run T05 once after reload before allowing normal output again.

### T09 — global emergency chord and mouse/tray recovery

The focused-console witness cannot observe an output transition after a mouse click
moves foreground focus away from its console. Test the paths separately and state
that boundary in the record.

1. Re-identify the Targus, enable normal output, and start the T06 `basic` witness.
2. Keep the console focused, hold Targus Numpad 1 through repeat, and while it is
   still down press `Ctrl+Alt+Shift+F12` on the unselected primary keyboard. Release
   the Targus key afterward.
3. Record an exact F24 down/up cycle, no final held output, Tappy disarmed, Rehearsal
   restored, and the emergency-stop status. A passing `basic` record does not encode
   the chord ordering, so the operator record must state when the chord was invoked.
4. With no output held, click the window **Emergency stop** and confirm it remains
   mouse reachable.
5. Click **To tray**, verify the window hides, use tray **Show Tappy**, hide it again,
   invoke tray **Emergency stop — release Tappy output**, and show Tappy again.
6. Record whether the global chord was unavailable due to a registration conflict.
   A conflict is a failed chord check, not permission to omit mouse/tray recovery.

Acceptance: the global chord functionally disarms while focus remains in the safe
target; window and tray emergency commands remain mouse accessible; tray hide/show
recovers the UI; every stop requires deliberate re-identification before mapping.

### T10 — unplug while mapped output is held

Re-identify Targus, enable normal output, and run:

```powershell
dotnet run --project tools/Tappy.OutputWitness/Tappy.OutputWitness.csproj -c Release --no-build -- --arm --ack-focused-console-only --ack-no-device-attribution --ack-tappy-mode-set --scenario held-unplug --original-key NumPad1 --output-key F24 --timeout-seconds 120
```

Keep the console focused. Press and continue holding physical Targus Numpad 1. Once
the witness reports that both source and output are held, unplug the Targus without
releasing the key. Release the physical key only after it is disconnected.

Acceptance: exit code 0; source down observed with no source up; exactly one F24
down followed by one synthetic-cleanup F24 up; final output not held; evidence
outcome `Passed`; Tappy shows the selected controller removed, all Tappy-owned
output released, Rehearsal restored, no controller confirmed, and fail-open
pass-through. Any timeout, lost console focus, physical source-up before unplug, or
second F24 transition invalidates this run.

### T11 — reconnect and selected-device isolation

1. Reconnect the Targus and wait for the picker to refresh; click **Refresh** if the
   arrival was missed.
2. Record that nothing arms automatically. Re-identify and confirm it.
3. Confirm the saved F24 mapping returns for the same persistent identity and run one
   Rehearsal cycle.
4. Record Tappy's aggregate event count. Press/release one reviewed harmless control
   on the K15, Tartarus, and primary keyboard. The Targus-selected aggregate count,
   layout, pressed summary, and output count must not change.
5. Press/release Targus Numpad 1 and record that only it advances the selected-device
   state.

### T12 — closeout and narrow milestone decision

1. Restore Rehearsal Mode, release all controls, record **Down: None**, and invoke
   Emergency stop once.
2. Exit Tappy from the tray, restore Num Lock/vendor profiles, and close blank
   Notepad without saving.
3. Hash the manual record, fresh probe output, each Output Witness JSON, and the
   profile snapshot. Run the privacy review before copying any evidence.
4. Mark the narrow physical gate `Pass` only if T01-T11 each passed on the same
   attributable device/build, all output-witness assertions passed, final output is
   released, and no limitation contradicts a first-milestone claim. Otherwise mark
   `Fail` or `Blocked` with the exact first failing check.

## Part B — K15 and Tartarus promotion runs

These runs follow the Targus witness and do not replace it. Start a separate record
for each device. Keep every result pending until performed.

For K15 select `Keyboard VID 1A2C PID 2D43`. Inventory the physical unit itself,
then exercise the ordinary keyboard controls twice in Rehearsal Mode, one at a time.
The reference shell shows 39 labeled positions, but `Fn`, lighting/mode buttons, and
firmware macros may be device-local or may emit non-keyboard behavior. Record the
physical-label-to-Tappy-control relation actually observed. For one ordinary key,
test repeat, a two-key hold, a four-key hold, rapid alternation, pass-through,
reconnect, and no duplicated Tappy transition from its four grouped interfaces.

For Tartarus select `Keyboard VID 1532 PID 0201`. Record the active Razer/Synapse
profile, then sweep numbered keys and each reviewed harmless thumb/wheel/directional
control. Record which controls reach either of the two grouped keyboard interfaces.
Mouse, consumer-control, system-control, and vendor-HID collections are outside the
current generic provider; a missing event there must remain an explicit unsupported
boundary, not a fabricated keyboard result or whole-device pass.

For each device also verify target-only identification and selected-device isolation
against the other attached controllers. A normal-output/unplug claim needs the same
Rehearsal, normal, external output, cleanup, reconnect, and profile evidence as
T05-T11. The current Output Witness accepts only `NumPad0`-`NumPad9` original keys;
therefore it can be reused only if a reviewed harmless vendor profile makes the
chosen physical control emit that exact original key. Otherwise a separately
reviewed finite witness extension is required. A visual Tappy tile or F-key action
label alone is not output evidence.

Do not promote either whole product from keyboard-subset observations. Full
`Functional` or `Verified` promotion also needs the applicable per-control Passport
and HIL matrix in [`HARDWARE_TEST_STATION.md`](HARDWARE_TEST_STATION.md).

## Part C — finite Logitech G13 input verifier

Run this only after the keyboard-device work, with Tappy and all mapped actions
closed. From a restored/built `F:\TAPPY` revision, inspect unarmed help first, then
explicitly arm one finite run:

```powershell
dotnet run --project tools/Tappy.G13Hil/Tappy.G13Hil.csproj -c Release --no-build -- --help
dotnet run --project tools/Tappy.G13Hil/Tappy.G13Hil.csproj -c Release --no-build -- --arm --timeout-minutes 30
```

The operator follows only the current prompt. In a clean run the sequence is:

1. neutral frame/all controls released;
2. one G1 press/release identity handshake;
3. two press/release cycles for each prompted control, in tool order: G1-G22, five
   LCD/menu buttons, M1/M2/M3/MR, joystick left-side, joystick bottom-side,
   joystick press, Lights, then Stick left/right/up/down;
4. simultaneous `G1 + G2`;
5. simultaneous `G3 + G4 + G5 + G6`;
6. simultaneous `G7 + M1 + Joystick press + Stick right`; and
7. hold G1, sweep fully left/right/back to center twice, then release G1.

For stick directions, “press/release” means cross the prompted direction's threshold
and return to neutral; do not press an unrelated joystick button. An accidental
control, repeat/duplicate transition, imbalance, unplug, lock/suspend, provider
fault, Ctrl+C, or timeout prevents a pass.

Acceptance: exit code 0 and `g13.tappy-hil.json` reports `Passed`, exact
`046D:C21C`/`FF00:0000`, one controller, 39 code-defined controls, 78 completed
per-control cycles, all four directions, all three simultaneous sets, balanced
transitions, zero unexpected/duplicate/unbalanced transitions, no disconnect/fault/
lifecycle interruption, within timeout, and completed capture cleanup. A perfect
no-retry run has 94 accepted presses and 94 accepted releases. Review every boolean
assertion; do not rely on exit code alone.

The artifact must continue to say `evidenceScope: input-functional` and
`compatibilityTierClaimed: none`. It proves no G13 mapped output, pass-through,
latency, unplug cleanup, full Controller Passport, or whole-device compatibility
tier. Those require a separate app/output HIL run.

## Current observability limits

The following cannot be promoted from the present UI or narrow verifiers alone:

- Tappy's UI does not display F13-F24 output transitions. The focused-console Output
  Witness is required for the Targus Rehearsal, normal, and held-unplug checks, and
  it deliberately makes no device-source attribution.
- The UI shows aggregate count, current pressed set, simultaneous count, and last
  transition, but no durable per-control cycle/repeat/timing history. The manual
  record must capture observations; there is no generic Passport writer yet.
- Scan/E0/E1 identity is encoded in the saved `ControlId`, not shown on the tile.
  Save and inspect the sanitized profile rather than inferring identity from a
  printed keycap.
- There is no generic `.tappy-passport.json` or keyboard `.tappy-hil.json` producer,
  no routing-latency p50/p99 recorder, and no external mechanical loopback. The
  documented latency target cannot be claimed from this run.
- Only one controller can be confirmed in the current app session. Connected-device
  isolation can be checked, but two simultaneously armed controllers and
  identical-device rebinding cannot be proven here.
- The current UI has no layer switch/profile replacement command, so physical
  layer/profile replacement during a hold cannot be exercised through it.
- The focused-console witness loses visibility when a mouse action moves foreground
  focus. It cannot by itself prove output-up timing for a window/tray click; use it
  for the global chord and use the separate mouse/tray availability checks described
  above.
- The G13 verifier intentionally invokes no mapping, sends no output report, and
  treats unplug/lifecycle interruption as failure. Its pass cannot close any output,
  pass-through, latency, or unplug-cleanup requirement.

These are evidence boundaries, not permission to mark a check passed. Keep the
broader fields pending in compatibility and testing documentation until an
appropriate attended run and reviewed artifact exist.
