# Controller Passport and HIL test station

No first-milestone automated test creates a hardware certification. A report may be
marked physical only when the operator explicitly starts a session and events arrive
from a real selected Raw Input handle. Descriptor enumeration and deterministic
tests are not physical control evidence.

The attended [`FIRST_MILESTONE_OPERATOR_RUN.md`](FIRST_MILESTONE_OPERATOR_RUN.md)
defines the narrow one-Targus completion witness, exact Output Witness commands, and
evidence fields. The wider Passport/HIL gates below apply to compatibility promotion
and must not be represented as already completed or silently substituted for that
finite milestone run.

## Controller Passport gate

For every control in the reviewed layout, capture at least two physical make/break
cycles. Also capture OS repeat on a held key, representative simultaneous groups,
maximum observed rollover, rapid alternation, Num Lock variants, reconnect,
unplug-while-held synthetic release, and a final all-released state. Record sanitized
model/instance fingerprints, identity confidence, Windows version, layout version,
requirements, and percentile calculations in `.tappy-passport.json`.

For a numpad, explicitly cover top-row versus numpad numbers, navigation behavior
with Num Lock off, operators, decimal/delete, and extended numpad Enter. For a
gaming/macro keypad, cover any keyboard, consumer-control, and vendor collections
without counting one physical press twice.

## HIL gate

With at least one harmless mapping assigned, run ten cycles per representative
control, simultaneous holds, shared modifiers, hardware repeat, two-controller
isolation, reconnect, unplug while output is held, emergency stop, Windows lock/
unlock, suspend/resume, profile/layer replacement during a hold, and shutdown.
Measure Raw Input receipt to completed output dispatch at the documented software
boundaries. Store p50/p99 and sample count in `.tappy-hil.json`.

Initial performance targets are median below 1 ms and p99 below 5 ms for a simple
mapping. A functional run that misses the target is labeled performance review; it
is not silently promoted. Mechanical-to-application latency needs an external
camera/electrical loopback and must not be inferred from software timestamps.

## Logitech G13 finite input verifier

`Tappy.G13Hil` is a narrow input-functional verifier for exactly one physical
Logitech G13: USB `046D:C21C`, Raw Input `RIM_TYPEHID`, usage page/usage
`FF00:0000`. It rejects the `046D:C232` G HUB virtual keyboard and refuses to start
unless the operator supplies the exact `--arm` flag. Restore the solution first,
then inspect help without starting capture:

```powershell
dotnet restore Tappy.slnx
dotnet run --project tools/Tappy.G13Hil/Tappy.G13Hil.csproj -c Release -- --help
```

Only an operator intentionally performing the physical test should run:

```powershell
dotnet run --project tools/Tappy.G13Hil/Tappy.G13Hil.csproj -c Release -- --arm
```

The optional `--timeout-minutes <5..60>` overrides the finite 30-minute default.
The guided run requires neutral state, an identity handshake, two press/release
cycles for each of the 39 code-defined controls, three simultaneous-control sets,
balanced transitions, all four stick directions, and a duplicate-suppression
sweep. Because all four directions are verified separately before the sweep, brief
perpendicular-axis crossings from the physical analog stick are tolerated during
the final horizontal sweep. A balanced wrong-control attempt resets only the current
prompt and preserves earlier completed prompts; repeated device cross-talk still
cannot complete a clean prompt. Duplicate or unbalanced normalized transitions remain
fatal assertions. Ctrl+C, timeout, fault, lifecycle interruption, or unplug disarms capture
and prevents a passing result.

The tool sends no G13 output reports, invokes no mapped actions, and measures no
pass-through, mapping-output, or latency behavior. It writes only aggregate
input-functional evidence under
`artifacts/hil/<random-run-id>/g13.tappy-hil.json`; it retains no raw reports,
device paths, ContainerIds, control chronology, or typed text. Its 25 automated
tests validate the verifier's state machine, refusal, retriable-prompt behavior, and
evidence contract. The exact attached G13 completed a clean passing physical run on
2026-09-07. That result is one input-functional record, not by itself a full
Controller Passport, full HIL certification, or `Verified` support.

## Vendor-HID/MIDI controller mapped-output witness

`Tappy.OutputWitness --scenario controller-basic` and
`--scenario controller-held-unplug` are finite output-only companions for controls
such as G13 buttons or MIDI pads that do not also appear as ordinary console source
keys. With the real Tappy application in normal-output mode and the chosen control
mapped to an allowlisted F13-F24 key, the operator keeps the witness console focused
and operates exactly that physical control. The basic scenario verifies one normal
press/release. The held-unplug scenario waits for output-down, instructs the
operator to keep the control physically held while unplugging its device, then
requires the cleanup output-up. Both require zero transitions from the selected
NumPad guard, a two-second duplicate/late-output quiet window, final output released,
and clean console teardown.

The JSON deliberately records `deviceSourceAttribution: none` and
`compatibilityTierClaimed: none`; the attended operator record is the attribution
and physical-action layer. The output-only stream cannot distinguish an ordinary
physical release from a truthful held-unplug cleanup without that attended record.
It also cannot prove source pass-through or Rehearsal recognition from silence.
The exact G13 G1-to-F24 hold-until-release path passed this scenario on 2026-09-07
with SHA-256
`8D1AA1A6F25A5FC197A5D9FA79007C81017453D6B0B2CDED15323CAF60F5E84A`.
This remains one narrow mapped-output record, not a full HIL or Passport promotion.

## Required initial physical matrix

- One standard USB numpad.
- One gaming or macro keypad, including separate K15 and G13 runs when claiming
  either model.
- Two simultaneous keyboards/controllers; identical models when practical.
- Pass-through observation in a harmless text target.
- Mouse/tray recovery and unaffected emergency chord.

Until stored evidence exists, `docs/COMPATIBILITY.md` must show no verified devices.
