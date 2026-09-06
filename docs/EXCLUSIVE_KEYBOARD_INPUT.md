# Exclusive keyboard input

> Status: architecture, activation policy, unsigned KMDF lab scaffold, managed
> driver wire client, and status-only broker/IPC scaffold implemented; nothing is
> installed, shipped, or active

## Product outcome

Tappy will let the owner designate one ordinary keyboard as the **protected primary
keyboard** and one different keyboard/keypad as a **configurable secondary
controller**. In exclusive mode, physical packets from the secondary controller are
copied into Tappy and removed before Windows applications receive the original key.
The protected primary keyboard always keeps its normal Windows behavior.

This is not possible with Raw Input alone. Raw Input can identify which device sent a
key, but it observes the packet after the keyboard stack has already decided how to
deliver normal input. Tappy therefore needs a real keyboard-stack filter for this
optional mode. Pass-through remains the default and works without the driver.

## Components and trust boundaries

```text
secondary keyboard
  -> KMDF upper device filter (copy packet; conditionally suppress original)
     -> KbdClass only in pass-through mode
     -> bounded nonpaged event ring
        -> LocalSystem Tappy.InputBroker through restricted device interface
           -> authenticated local IPC
              -> ordinary non-admin Tappy app and mapping engine

protected primary keyboard
  -> its filter instance is permanently configured pass-through
  -> KbdClass -> Windows applications
```

The filter is attached per keyboard device instance. It intercepts the documented
keyboard class service-callback connection in the same architectural position as
Microsoft's [Kbfiltr sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/keyboard-input-wdf-filter-driver-kbfiltr/),
but the Tappy implementation must support USB HID keyboard stacks and must not copy
the sample's PS/2-only initialization or ISR behavior. The filter does no mapping or
injection. Its complete job is to pass packets, copy packets into a bounded queue,
and suppress a specifically armed secondary instance while its fail-open lease is
healthy.

The WPF process never opens a kernel device or runs as administrator. A separately
installed broker owns the restricted driver handle and exposes only sanitized,
bounded input events over authenticated local IPC. The driver interface does not
accept pointers, executable data, arbitrary registry paths, process identifiers, or
general memory operations.

## Identity and activation

Enumeration is not authorization. Setup requires two independent physical
press-and-release checks:

1. Choose the keyboard used for normal typing and identify it physically. Tappy saves
   it as the protected primary keyboard.
2. Choose a different keyboard/keypad and identify it physically. Tappy saves it as
   the configurable secondary controller.
3. Confirm that a mouse/tray recovery path works and that the emergency stop can be
   invoked from the protected primary keyboard.
4. Review the exact driver package, accept an administrator prompt, and install the
   optional component separately.
5. Re-identify both keyboards after installation. Exclusive mode remains unavailable
   until the broker and both driver instances acknowledge the same roles.

Stable instance identity is mandatory. A session-only or ambiguous device cannot be
armed because identical serial-less keyboards could exchange roles after reconnect.
VID/PID alone is never sufficient. ContainerId may group collections belonging to one
physical keyboard, but it does not distinguish two separate identical units by
itself.

## Fail-open state machine

The kernel's default and reset state is `PassThrough`. `CaptureAndSuppress` exists
only while all gates remain true:

- the exact primary and secondary instances are connected;
- the primary instance acknowledges `ProtectedPrimary/PassThrough`;
- the secondary acknowledges `ConfigurableSecondary/CaptureAndSuppress`;
- the broker authenticated through the restricted interface;
- the broker heartbeat is newer than the negotiated 250–2,000 ms watchdog;
- the WPF app has a confirmed controller and reports a working recovery path;
- the signed-build, HVCI, boot-configuration, and protected-application gates pass.

Heartbeat expiry, broker/service exit, app exit, emergency stop, session lock,
suspend, PnP removal, invalid protocol data, queue overflow, role mismatch, or driver
fault restores pass-through inside the driver. Recovery must not depend on WPF code
executing. The broker waits for a pass-through acknowledgement before reporting that
exclusive mode is off.

The deterministic policy lives in
`src/Tappy.Core/Input/ExclusiveInputSafetyPolicy.cs`. The version-1 bounded IOCTL and
event contract lives in
`src/Tappy.Windows/ExclusiveInput/TappyKeyboardFilterProtocol.cs`, with the matching
native ABI in `driver/Tappy.KeyboardFilter/tappy_filter_public.h`. The unsigned lab
driver now implements a SYSTEM-only, single-client raw PDO, tokenized session,
strictly increasing policy generation, 1,024-event fixed ring, and kernel-local
watchdog. The managed device-session codec is tested. A separate broker executable
uses the supported Windows-service host, a LocalSystem-plus-one-user pipe ACL,
local-computer rejection, a 256-bit installer-provisioned key, authenticated bounded
frames, and strict sequences. It exposes only an unavailable/status response today;
it deliberately has no arm, heartbeat, event, or driver-open command and is not
connected to WPF.

The managed broker coordinator performs a grouped two-phase arm: it opens every
filter instance belonging to the exact primary and secondary ContainerId groups and
requires every primary instance to acknowledge protected pass-through for generation
N; only then may any secondary instance request suppression for that generation.
Every secondary receives the heartbeat, the aggregate read stays bounded, and stop
fails open every secondary before touching any primary. A bad acknowledgement,
stale-generation event, heartbeat failure, or fail-open IOCTL failure closes every
handle, which independently resets the kernel instances to pass-through.

## Event semantics

The broker forwards scan code, E0/E1, make/break, unit ID, monotonic sequence,
and policy generation. It never translates input into text. The sequence must be
strictly increasing within each batch; batches contain at most 256 events. A policy
generation change invalidates older buffered events so a release from a previous
role cannot affect the new controller.

If a queue would overflow, the filter increments an aggregate overflow counter,
clears the exclusive lease, and returns to pass-through. It does not silently drop a
break packet while keeping suppression active.

## Installation and recovery requirements

- The standard Tappy installer does not include or enable the filter by default.
- “Tappy Exclusive Input” is a separately selected feature with an administrator
  consent screen describing system-wide keyboard impact.
- No installer step disables Secure Boot, Memory Integrity/HVCI, driver signature
  enforcement, antivirus, or anti-cheat.
- The package includes a signed uninstall path, Safe Mode removal instructions, and
  a documented command that restores the original keyboard stack.
- A recovery test must prove normal typing after service termination, app kill,
  forced watchdog expiry, hot-unplug, sleep/resume, Windows update, driver upgrade,
  rollback, and uninstall.

## Remaining implementation gates

1. Complete stable device-instance association and signed-client verification, then
   add the broker's currently absent arm/stop/heartbeat/event commands around the
   tested managed driver-session coordinator.
2. Add primary-keyboard setup and source-mode UI without making exclusive the default.
3. Replace the deliberately nonmatching development INF target only in a dedicated
   test package and prove per-device attachment/removal on a disposable driver lab.
4. Run Driver Verifier, IOCTL fuzz, PnP/power, HVCI, and destructive
   recovery testing on a dedicated test machine or VM—not the owner's only keyboard
   workstation.
5. Complete the signing, HLK, and anti-cheat matrices before any public binary claim.

See [driver signing and release](DRIVER_SIGNING_AND_RELEASE.md) and
[anti-cheat compatibility](ANTI_CHEAT_COMPATIBILITY.md).
