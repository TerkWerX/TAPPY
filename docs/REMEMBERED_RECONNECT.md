# Remembered controller reconnect

During a running Tappy session, an explicitly identified and confirmed controller
can return after USB removal without repeating its identification gesture. The
same eligibility also now survives Tappy closing and reopening (see "Reconnect
across an app restart" below) for the same restricted set of device classes.

- Removal still releases Tappy-owned output, clears the live photo/grid, and turns
  Rehearsal Mode on. It does not delete assignments or layout edits.
- When exactly one device with the same provider and persistent identity returns,
  the photo, controls, assignments, colors, positions, and sizes are restored.
- Recognition resumes only after native input is neutral. G13 requires an actual
  report; a held control must be released first. Until then the restored display
  explains that reconnect is waiting for release.
- Output stays in Rehearsal Mode. The user can turn it off to resume normal use.
  Interrupted macros and held outputs are never replayed by reconnection.
- Emergency Stop, standalone Windows lock/shutdown, input/output faults, and
  choosing a different controller for identification revoke automatic reconnect
  eligibility for the rest of that running session.
- Trust never grants live output by itself: reconnection (mid-session or across a
  restart) always lands in Rehearsal Mode and still requires the device to be
  physically neutral first. It is not an unattended, automatic-output feature.

Matching uses the existing persistent identity, never just a model name or recycled
USB handle. A port change may require identification again. Ambiguous identities
and duplicate matches are not automatically restored. WinMM MIDI identities
(Akai Fire, APC MINI) are marked ambiguous only as a fallback: Tappy now attempts
to resolve each MIDI device's real Windows hardware ContainerId (the same identity
property keyboards already use) via a documented OS-intercepted WinMM query, and
only when that resolution succeeds does a MIDI device become eligible for
reconnect, same-port-only, exactly like keyboards. When resolution fails for any
reason, a MIDI device stays exactly as ambiguous as before and still requires
manual identification every time. See D-026 in [`DECISIONS.md`](DECISIONS.md).

The UI polls only while a remembered reconnect is pending, including while Tappy
is minimized. Native arrival notifications also initiate restoration. Input capture
and engine activation remain separately checked; stale assignment editors cannot
save across the new confirmation generation.

Automated coverage includes immediate/queued UI presentation, a G13 held through
reconnect, restoration of customized layout/color and assignment, neutral keyboard
capture with a changed session handle, stale editor rejection, no replay/output,
and stop/lock/ambiguous/duplicate/wrong-device exclusions. Physical confirmation of
the new behavior is a separate attended test, not driver certification.

## Sleep/wake mode preservation (subsequent operator-requested behavior)

Sleep is now distinct from USB removal. For a recognized, unambiguous confirmed
controller, Tappy retains the photo, tiles, layout, assignments, and previous mode
through sleep, marking the controller paused rather than unplugged. Output is
internally suppressed, held outputs released, and running actions cancelled during
the transition even when the Rehearsal checkbox retains its unchecked setting.

After Windows resumes (and unlocks if it locked), the same persistent identity and
fresh neutral input must be verified before the previous operating mode resumes.
A held wake-up control must be released; its press and release do not run mappings.
Interrupted macros and held outputs are not replayed. A subsequent fresh press can
run its assignment normally. This does not depend on clicking Identify or Confirm.
An ordinary USB unplug/replug still restores in Rehearsal Mode as before.

Lock and suspend notifications can arrive in either order; duplicate events must
not overwrite the original mode. A standalone lock/unlock without an observed sleep
cycle retains the manual-rearm policy. Emergency Stop, faults, rejected output
cleanup, shutdown, or a new identification cancel sleep restoration. Ambiguous
devices are excluded. Sleep/wake preservation itself remains a within-one-process
feature; it is distinct from, and unaffected by, the cross-restart persistence
described next.

## Reconnect across an app restart

Cross-restart eligibility is deliberately narrower than mid-session reconnect: it
exists specifically for the case where the physical device never left the PC and
only the Tappy process itself closed and reopened.

- Each device-tab slot's saved profile records the persistent id of whichever
  controller it last confirmed (`RememberedReconnectPersistentId` in
  `TappyProfile`), written at the moment `ConfirmController` succeeds — whether
  that confirmation was a manual identification or itself a mid-session remembered
  reconnect. A slot with nothing ever confirmed has no such record.
- On the next launch, if that saved identity matches exactly one currently
  connected, unambiguous device for that slot's provider, the slot is armed as a
  remembered-reconnect candidate immediately — the same in-memory mechanism, same
  neutral-then-silent-confirm handshake, and same "no identification gesture is
  needed" status message as an ordinary mid-session reconnect. If the device is not
  yet present, the check is retried opportunistically on each later poll and native
  arrival notification, not just once at startup.
- Keyboard-class controllers and the Logitech G13 always have a stable,
  Windows-verified identity and are eligible. Akai Fire and APC MINI (WinMM MIDI)
  are eligible too, but only when Tappy could resolve that specific device's real
  Windows ContainerId at the time it was confirmed (see D-026); when it couldn't,
  that device stays ambiguous and excluded exactly as before — every app restart
  requires identifying it again.
- A protected typing keyboard is never restored this way, matching the mid-session
  behavior.
- Ending a session with Emergency Stop does not retroactively unsave an earlier
  successful confirmation's eligibility (the two are written at different times),
  but it does clear this session's own in-memory candidate immediately, and a
  fresh manual identification afterward overwrites the saved record with whatever
  is identified next (or leaves the old record in place if identification is
  started but not completed).
- Restoration still always lands in Rehearsal Mode. Nothing sends real output
  before the user takes a further action, identical to mid-session reconnect and
  ordinary manual confirmation.
- A multi-interface composite device (e.g. Razer Tartarus, which exposes several
  keyboard-usage-page collections across two USB interfaces) has each of its own
  interfaces reported to Windows separately, even though the whole unit never
  disconnected. Tappy's own provider classifies a second interface arriving for
  an already-known identity as a "membership changed" notification, not a fresh
  arrival. For an already-confirmed, actively used device this is treated as
  more suspicious than an ordinary disconnect and still wipes reconnect
  eligibility outright, requiring a fresh manual identification — that
  protection is unchanged. But for a candidate startup reconnect only just
  silently armed, which a human has not yet touched, this same notification is
  treated like an ordinary disconnect instead: eligibility survives and the
  retry already under way continues, rather than demanding re-identification for
  what is almost always the device's own startup enumeration settling. See
  D-020 in [DECISIONS.md](DECISIONS.md).

Automated coverage includes a full close/reopen cycle with the device continuously
connected (silent restore, saved layout/mapping intact), the saved device being
absent at restart (falls back to ordinary manual identification, then restores
normally once it does reconnect), confirmation that a MIDI identity whose
ContainerId could not be resolved never restores this way even when saved and
continuously connected (D-018's original fallback behavior, still exercised and
still correct), confirmation that a MIDI identity whose ContainerId *was*
resolved does restore the same way keyboards do (D-026), and a
multi-interface composite device surviving a benign membership-change
notification during startup reconnect without falling back to manual
identification.

Controller-triggered Windows wake is separate from app restoration: it requires
device/driver/firmware support. TESTBED's read-only wake inventory currently exposes
Logitech receiver interfaces but no G13 (046D:C21C) wake-enable instance. No Windows
power settings or firmware settings were changed. G13-button wake is not claimed
as working; waking by mouse still permits the app's sleep recovery to be tested.
