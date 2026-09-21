# Microsoft keypad Num Lock report diagnostic

This attended diagnostic compares two owner-triggered Num Lock passes for one
exact fingerprint and the verified Microsoft Bluetooth Number Pad model hint.
It does **not** change Tappy, bindings, Windows Num Lock state, Bluetooth pairing,
driver settings, or inject output. It is separate from the descriptor-only probe.

Launch with `--target-fingerprint <64 uppercase hex> --output-folder <absolute path>`.
The window opens disarmed. The operator checks that Tappy is in Rehearsal Mode
and clicks Start with the mouse. The provider's global neutral-key check is still
required. The capture uses target-only identification events, never confirms a
mapping target, and has no action-output implementation.

Pass 1 uses the existing Num Lock state. The operator finishes it, toggles the
physical pad's Num Lock once while capture is disarmed, starts pass 2, then finishes
and saves. Mouse-driven controls ignore native keyboard activation. Escape or
closing aborts. The tool remains visible after completion; it does not close before
the operator can read the outcome.

Each pass is limited to 120 seconds and 512 target packets. Only the 17 keypad
scan positions and numeric/navigation/operator/Num Lock/Clear virtual-key allowlist
are counted. Other devices are rejected before retaining any key data or even a
counter. Out-of-scope target reports contribute only a discard count. No typed
text, device paths/handles, timestamps per event, or chronological history are
saved. JSON contains sorted aggregate make/break/repeat counts by scan code,
extended flag, and Windows virtual-key code for each pass. Lock, sleep, shutdown,
target connection change, fault, timeout, or closing aborts without automatic
rearming. Output filenames are unique and use CreateNew, never overwrite.

Revision 2 fixes an overbroad connection-change guard: Windows can announce the
already-enumerated pad after Raw Input registration. Identical arrival/member
notifications (including display-name updates or reordered member lists) no longer
abort the pass. A real removal, changed session/member set/model/identity, or
unverifiable selected target still aborts. Finish remains enabled throughout an
active pass, independently of the now-locked rehearsal checkbox. An aborted
capture is visibly labeled **Capture stopped**, with red status text.

Completed means both passes were collected, not that key coverage or certification
passed. Aborted summaries stay marked Aborted. The raw-input provider may omit
unmatched releases by design; this diagnostic is intended for neutral-started
press/release sweeps, not lossless USB packet analysis.

Keep the diagnostic focused and use only the specified keypad. Rehearsal in the
separate Tappy process must be checked by the operator; this tool cannot verify or
change its live state. Native pass-through behavior is not blocked system-wide.

Tests link the same aggregate-session source into Tappy.Windows.Tests. The optional
`--render-preview <absolute PNG path>` is local developer visual QA only: it draws
the unshown form without enumerating or registering input and never arms capture.
