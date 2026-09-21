# TPE keyboard improvements adapted into Tappy

Source reviewed: `J:\RPG UGA`, TPE keyboard naming, identification, identity,
chord and Unifying regression tests. TPE itself was not modified.

## Included

- Query HID product strings with zero requested read/write access and shared
  handles, as TPE does. Sanitize display metadata and retain explicit known-model
  and receiver labels. Missing or conflicting product names use honest generic
  labels. Names never authorize input or alter stored identities.
- Protect an explicitly identified typing keyboard across every workspace tab.
  Use Tappy's existing selected-device press/release/neutral confirmation gate
  instead of registering another global identification listener.
- Persist protection separately from assignment profiles. Protecting or clearing
  leaves all tabs in Rehearsal. Protecting an active controller releases owned
  output and cancels its capture/reconnect without deleting assignments.
- A protected raw-keyboard identity is refused by controller identification,
  confirmation, reconnect and event dispatch. New tabs inherit protection.
- Invalid/unreadable saved protection blocks keyboard-controller selection until
  the user explicitly clears/reconfigures it. Saving failure preserves the old
  protection. Settings use version validation and atomic replacement.
- Adapted tests cover two identical Unifying receivers, wrong-device presses,
  unpaired/held identification, selection changes, retained mappings, output
  release, restart persistence, unreadable settings, save failure and no
  same-model reconnect guessing.

## Operator steps

1. Expand **Typing keyboard: not set** below controller confirmation.
2. Select the everyday typing keyboard/receiver in the device picker.
3. Click **Identify this device**, then press and release a key on that keyboard.
4. Click **Protect as typing keyboard**, **not** Confirm selected controller.
5. Select and identify the spare controller normally. Enable its output only when
   ready. The protected keyboard is visibly marked in the picker.

Changing protection uses the same physical verification for the replacement.
**Clear typing-keyboard protection** asks for explicit confirmation and does not
automatically arm the formerly protected device. The sidebar scrolls when expanded
settings exceed the available window height.

## Boundaries

This is application-level exclusion from Tappy assignments, not driver-based
suppression of native Windows input, and not driver certification. No driver or
Windows security settings are changed. Raw-input pass-through still applies.

Unifying protection covers the receiver's keyboard input as a whole; it does not
distinguish multiple keyboards sharing that receiver. Generic HID product strings
can still identify only the dongle; the subsequent [wireless-name discovery](WIRELESS_KEYBOARD_DISCOVERY.md)
adds C52B paired-keyboard names and Bluetooth paired-device names without changing
that input/protection boundary. A changed USB port may change identity and require
explicit re-identification. Unlike TPE's model-based fallback, Tappy never silently
retargets assignments or primary protection by VID/PID alone.

TPE's general HID-byte trigger shortcuts and keyboard chord engine were not copied
wholesale: arbitrary HID bytes do not guarantee balanced button releases, and chord
input requires a separate, deliberately designed integration. Tappy's existing
macro/keyboard output assignments are unchanged.

Automated and headless visual checks use synthetic devices/output. Physical
primary-protection and two-dongle acceptance on TESTBED remains to be performed.
