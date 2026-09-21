# Multi-device tabs and Targus PAUK10U test build

Implemented locally on 2026-09-18. This is application functionality, not a
driver-certification result or a claim that simultaneous physical pads have passed.

## Operation

- **+ Add device** opens an independent tab (up to 16). Confirm each physical
  controller in its own tab. Switching tabs changes the editor, not which
  confirmed controllers can produce output. Hidden tabs remain live.
- Tab headers show the device slot and Rehearsal/Normal output state. Rehearsal
  applies per tab. Starting identification puts every tab into Rehearsal; normal
  output cannot be enabled while identification is active in any tab.
- Device selection in the tab strip is mouse-controlled. Key down/up and text
  search cannot change the selected tab or activate Add device while focus is
  in that strip. Plain Tab/Shift+Tab can leave the strip. This guards against
  pass-through keypad Home/Left/Backspace navigation; it is not per-device input
  blocking and does not suppress native input in other applications or editors.
- Emergency Stop, the emergency hotkey, tray stop, and application shutdown stop
  every tab. A controller reserved by one tab cannot also be identified in another.
- Identical keypads are distinguished by device identity, not model VID/PID.
  The picker adds a short hashed port identifier when identical models coexist.
  Physically identify each pad. Serial-less pads moved to different USB ports may
  need identification again; Tappy must not guess which pad owns which profile.
- During the running session, unplugging a remembered controller hides its tab.
  The session/profile slot remains reserved, polled, saved, and included in stop
  and cleanup operations. Reconnecting the same unambiguous identity restores the
  original tab in slot order and in Rehearsal Mode. A returning tab does not steal
  selection from another visible tab. If the selected tab disconnects, selection
  moves to the first remaining tab. With none left, an empty-state message keeps
  **+ Add device** available without editing an absent controller's profile.
  Sleep/lock retained sessions, new setup tabs, and safety faults requiring
  attention are not hidden by this rule.
- Tab count survives restart. Slot 1 retains the existing profile store;
  subsequent stores are under `%LOCALAPPDATA%\Tappy\device-tabs\device-N`.
  Layouts and assignments are separate. Each slot independently restores its own
  last-confirmed device without an identification gesture if that same
  unambiguous device is still connected at startup (see
  [remembered reconnect](REMEMBERED_RECONNECT.md)); otherwise startup requires the
  ordinary manual confirmation. Either way it always starts safely in Rehearsal.
- The native Raw Input listener and keyboard/MIDI action ownership are shared.
  Cleanup is scoped to the device tab, so releasing or unplugging one controller
  does not release a key still owned by another controller.
- Workspace zoom (Ctrl+scroll on the grid) and the photo-pane splitter width are
  saved per tab, as part of that tab's own layout — switching tabs re-applies that
  tab's own values to the shared visual elements rather than leaving whichever
  values a different tab last set. See D-019 in [DECISIONS.md](DECISIONS.md).

## Targus PAUK10U

The owner confirmed the silver PAUK10U model with Backspace and 000. The observed
Raw Input model identity is VID 05A4 / PID 9862. That model now receives its photo
and all 30 assignment/reference boxes immediately upon confirmation: the physical
keypad arrangement plus separate navigation and standard-Enter report boxes.
Num Lock and **000** are nonassignable reference tiles. Keypad Enter spans two rows. Existing custom positions, assignments, and
unexpected learned controls are retained.

The standard keys use scan codes rather than Num Lock-dependent virtual keys.
The owner confirmed that pressing 000 highlights ordinary 0. Its exact event
sequence and repeat count remain unverified; no unique scan code or timing-based discriminator is invented. It
cannot be assigned independently until a reliable signal is established.

The owner-selected straight-on `TargusmodelPAUK10U ITEM NO 10.png` photo is
embedded byte-for-byte, with a runtime crop of empty horizontal margins to
670 by 880 pixels. The original skewed photograph is no longer embedded. All
19 photo highlights have disjoint inset key-face rectangles, including a tall
Enter rectangle. No pad colors were changed and no addressable RGB
capability is advertised. Photograph identity does not prove hardware support or
third-party redistribution rights.

### PAUK10U alternate reports / photo correction

Saved TESTBED profiles from both physical pads show E0 navigation reports for
Num Lock-off (Home, arrows, Page Up/Down, End), and non-E0 Enter and Num Lock.
The photo catalog now groups these alternate reports onto their existing
physical key faces, including the standard Insert/Delete variants. The button
highlight template observes the aggregate selected/illuminated state of every
report associated with the face; previously it incorrectly watched only the
first associated tile. Assignment bindings and saved grid positions are not
rewritten or merged by this display fix. Existing numeric/navigation assignments
remain distinct. There are still only 19 physical highlight regions.

The saved profiles identify controls but do not record their event sequences.
No unique 000 report appeared in the saved controls. A focused physical capture
is still required to establish the complete sequence, and its reference tile is
still nonassignable. Do not infer triple-zero presses from typing speed.

### PAUK10U assignment-grid repair

Layout v2 repairs the older mixed-size grid once on confirmation. Generic tile
sizes inherited at the keypad's tighter spacing are replaced with keypad sizes;
legacy collisions are moved into orderly extra rows below the main keypad.
Alternate Num Lock/Enter/navigation reports are retained in that extra area,
not merged or deleted. Their assignment bindings and colors are retained.

Newly learned reports receive explicit unoccupied positions, and the UI reads
those positions immediately instead of falling back to collection order. The
placement check accounts for resized controls and the tall Enter key. After the
v2 layout is saved, later custom positions and sizes survive reconfirmation.
Save the profile after checking the repaired arrangement.

### PAUK10U simultaneous numeric and navigation assignments

The v4 layout keeps numeric keys, navigation keys, and one shared Enter box
visible together. A physical press highlights its exact report box and the
corresponding shared photo key. Nothing swaps assignments or removes boxes when
Num Lock changes. Numeric and navigation reports retain independent bindings in
the same layer. Both can be edited at any time, including numeric 5.

- Shared operators appear once. The owner observed the same base Enter in both
  modes: it is now the original tall **Enter (shared)** box. The redundant E0
  Enter box is hidden, including on reconnect and when old reports are restored.
  Its existing profile layout/binding data is retained without merging or
  overwriting the shared Enter assignment. Only observed base Enter selects the
  shared box; an unexpected E0 report cannot resurrect a second box.
- Num Lock reports are ignored for identification, macro execution, and physical
  highlighting on this model: the owner observed them accompanying ordinary
  keys. This does not suppress native Windows input. Older bindings are retained
  in saved profiles but remain inactive for these ambiguous reports.
- 000 remains a reference marked “Shares 0 input.” No timing heuristic invents
  an independent key. The owner reports neither 5 nor 000 emits when Num Lock is
  off; keeping their boxes visible cannot make the hardware emit missing events.
- The photo LED reflects the last observed numeric/navigation report, not live
  hardware LED state. Num Lock alone and shared operators never toggle it.
  Unknown is grey, numeric is bright green, navigation is dark green.
- Each keypad has independent observed-mode state. No synthetic Num Lock events
  are sent. Editing captures the actual report, controller and layer; changing
  physical mode does not redirect or prevent saving that draft.
- On the owner's request, v4 repairs known older positions once into compact
  side-by-side groups, numeric left and navigation right. Rows and columns match
  the physical keypad, including the gaps at 5 and 000 and Delete below Page Down.
  The 29 visible boxes fit in a 712-by-480 logical-pixel area. Colors, IDs and assignments
  are preserved; older known-key positions are intentionally reset. Subsequent
  v4 custom positions/sizes persist. Unexpected extra reports remain preserved.
  Small windows or a very wide photo pane may still need resizing or scrolling.

Automated coverage checks permanent report boxes, stable positions/labels,
separate saved assignments, both Enter reports, editing across mode changes,
balanced held output, Num Lock synchronization suppression, per-device isolation,
and headless rendering in all themes. This is not physical hardware verification
or a driver-certification result.

## Verification and next physical check

### Readable device choices

Saved slot counts no longer create visible Device 1/Device 2 tabs on startup.
Profiles load silently; setup and typing-keyboard protection remain accessible
without an active tab. Identification opens that slot, and + Add device reuses
an idle saved slot before allocating another. A disconnected session with a
reserved reconnect identity is never reused. Unplug/reconnect still hides/restores
only the affected active tab and preserves its assignments.

The picker names the verified Targus PAUK10U and Razer Tartarus keyboard IDs,
labels the Freewolf K15 ID as a candidate, and describes the Logitech Unifying
receiver as a receiver rather than guessing its paired keyboard model. Microsoft
eHome infrared interfaces are excluded from the controller picker using their
exact interface family, never the shared system-container ID alone. They remain
enabled in Windows. The same filter applies to hot-plug discovery, so these
interfaces cannot reappear on refresh or arrival. Unrecognized keyboards remain
available and explicitly unknown.
Identical choices receive short stable device-ID suffixes, not misleading USB
port numbers or enumeration-order numbers. Selected-device details include the
hardware IDs, identity limitations and the physical identification instructions.
Names do not change routing, stored identities, confirmation, or reconnect logic.

Automated tests cover independent two-pad events; duplicate-device rejection;
identification/output interlock; shared F24 ownership; unplug/reconnect of only one
pad; global stop; saved per-slot profiles and custom positions; and action scope
cleanup/retry. Headless WPF checks exercise tab switching, theme contrast on tab
headers, and the cropped photo resource without opening native input capture.

Next on TESTBED: keep both tabs in Rehearsal, confirm one Targus in each tab, verify
all tiles and photos appear before pressing every key, verify presses remain in
the correct tab, then inspect the 000 signal. Physical output, simultaneous holds,
and unplug recovery need their own evidence on this new binary. Prior G13 results
remain historical evidence for the earlier binary, not certification of this one.
