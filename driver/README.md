# Tappy optional keyboard filter

This directory contains the optional KMDF path for per-device keyboard
exclusivity. It is intentionally separate from `Tappy.slnx`: ordinary Tappy
builds and pass-through operation never require a driver toolchain.

## Current stage

`Tappy.KeyboardFilter` is a buildable USB-HID keyboard upper-filter development
scaffold. Its raw control child is SYSTEM-only and single-client. Capture and
suppression require a versioned session token, a strictly increasing policy
generation, the configurable-secondary role, and a 250–2,000 ms heartbeat. The
fixed 1,024-event nonpaged ring fails open on overflow; heartbeat expiry, broker
handle close, invalid lease state, and an explicit emergency request also clear
the ring and restore pass-through inside the kernel. The INF uses the deliberately
non-retail ID `TAPPY\KeyboardFilterDevelopmentOnly`, so it cannot attach to one
of the owner's keyboards by matching VID/PID.

This is still lab code, not a shippable driver. It has not been installed, loaded,
fuzzed, run under Driver Verifier, or exercised through the destructive recovery,
HVCI, HLK, and anti-cheat matrices. The Tappy app does not enable it yet.

## Reproducible build

The official Microsoft WDK and SDK NuGet packages are pinned in
`driver/packages.config`. Restore and build from a normal PowerShell prompt:

```powershell
.\eng\Restore-DriverDependencies.ps1
.\eng\Build-KeyboardFilter.ps1 -Configuration Release
```

The unsigned output is placed below `artifacts/driver`. Building does not install,
load, sign, or register the driver. Do not install development bits on the daily
gaming workstation; use a disposable VM or dedicated driver-test computer.
