# Controller bank controls

Button combinations and two-bank toggles are described in [Controller combinations](CONTROLLER_COMBINATIONS.md). The user confirmed single-key Microsoft numpad → Tartarus bank/LED control before this extension; physical combination verification is separate.

Tappy can use a physical button on one confirmed controller to change the
assignment bank on that same controller or another confirmed controller. The
visible tab and foreground application do not determine the destination.
This is internal bank navigation, not a new output macro or input-blocking driver.

## Setup

1. Confirm both controllers in their own device tabs.
2. Select the source button and open **Bank control…** below the assignment editor button.
3. Choose the destination by its name and device ID, then **Next bank**,
   **Previous bank**, or **Go to bank** and a specific bank.
4. Click **Use bank control**. Press and release the source button to operate it.
5. For a two-button setup, repeat with another source button and the opposite direction.

Next/previous wrap around the destination's saved banks. Original Tartarus profiles
have eight banks; other current profiles normally have three. The bank picker in
the layout header exposes the existing banks for any confirmed controller. These
are Tappy assignment banks, not a change to a keypad's physical Num Lock mode.

A bank-control button is reserved across **all source banks**. Its underlying
keyboard/macros remain intact, but do not execute while the bank control is set.
Use **Remove bank control** to restore them. The ordinary assignment editor can
still edit those underlying assignments and identifies when an override is active.
Only the destination bank changes. No extra controller/model support (including
the user's HOTAS analogy) was added as part of this work.

## Safety and connection identity

- One valid physical press/release issues one request. Repeats, orphan releases,
  disconnect cleanup, emergency stop and lifecycle cleanup cannot issue requests.
- Navigation works in Rehearsal Mode; external macros remain suppressed.
  Original pass-through input can still reach Windows and other applications.
- Destinations use provider plus persistent device ID, never display name, tab
  position, keyboard focus or a same-model fallback. Queued UI work also validates
  the exact source and destination confirmation tokens before changing a bank.
- Legacy WinMM MIDI inputs lack reliable persistent physical identity. They can
  be sources and destinations, but an ambiguous destination is pinned to its
  explicitly confirmed session. Reselect it in the bank editor after reconnect
  or restart. An unavailable saved destination is never silently substituted.
- If target controls are held, or the previous LED synchronization/bank save is
  still busy, the request is refused with a message. It is not replayed later.
  Device-identification sessions, unsafe output cleanup, and open assignment/bank
  editors pause routed bank controls.
- Tartarus physical indicators follow only the **destination's** existing
  **Follow bank changes** opt-in and verified native protocol. Other devices'
  lighting is untouched. Lighting failure does not claim successful synchronization.
- Bank selection and bank controls are saved with profiles. Profile schema is now
  **4**; schema 3 files upgrade on load without removing layouts or macros. Keep
  a pre-upgrade backup before returning to an older executable, which cannot read
  schema 4. Exported destination identities are local references, not portable
  authority to control a different person's hardware.

## Automated verification, 2026-09-18

642 solution tests pass. New coverage checks next/previous/direct/self navigation,
all-source-bank overlays, macro preservation and restoration, JSON snapshots,
Microsoft-to-Tartarus routing with two identical destination models, target-only
LED follow, both MIDI/keyboard directions, session-bound MIDI targets, reconnect
and stale-source rejection, held-key output cleanup, busy targets, editor pause,
and actual WPF picker updates using the production queued-dispatch pattern.
Bank editor selection text and button layout were checked in dark, light and
high-contrast themes, including a 520×570 layout rendered at 125%.

An attended physical Microsoft-pad/Tartarus run is still required. Confirm that
the assigned source buttons change the Tartarus banks/LEDs while the pad's bank
stays put, that F24/F22 remain isolated in their respective Tartarus banks, and
that disconnecting the target does not redirect a command to another controller.
This work does not certify a driver or change any certification result.

## TESTBED handoff

Staged (not launched) at `C:\TappyLab\20260918-cross-controller-banks\app\Tappy.exe`.
The existing **Tappy Tartarus LEDs Test** desktop shortcut now points to this build.
Executable SHA-256 verified on both machines:
`6C525F28E6256B547C6856C96949591C3A39484787584135109D8BE2B401212C`.
The prior executable was not overwritten. With Tappy closed, its seven local-data
files were backed up to
`C:\TappyLab\20260918-cross-controller-banks\profiles-before-bank-controls`,
and the previous shortcut was saved beside that directory. No hardware commands,
driver installation, security-policy changes or GitHub updates were performed.
