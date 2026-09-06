# Driver signing and release gate

> Status: release plan established; reproducible x64 WDK and status-only broker
> builds are healthy; neither has been installed, loaded, test-signed, or shipped

## Current toolchain audit

The 2026-09-06 publication audit finds Visual Studio Community 2026 18.9.2, the
x64 MSVC 14.51.36231 compiler, Windows SDK `10.0.28000.0`, and SignTool. Tappy pins
Microsoft's official `Microsoft.Windows.WDK.x64`, `Microsoft.Windows.SDK.CPP`, and
`Microsoft.Windows.SDK.CPP.x64` packages at `10.0.28000.2526`. The traditional
machine-wide WDK is absent, but the official NuGet WDK path is complete and
`eng/Test-DriverToolchain.ps1 -Require` reports ready.

`eng/Build-KeyboardFilter.ps1 -Configuration Release` restores those pinned packages
after checking their recorded SHA-256 values and NuGet signatures, then builds the
x64 SYS/INF/CAT with 64-bit MSBuild. The current unsigned lab build and WDK
code-analysis pass both report zero errors and zero warnings. Build output is ignored
below `artifacts/driver`; it is not a release artifact and has never been installed
or loaded.

The separate `Tappy.InputBroker` project also builds cleanly as a self-contained x64
Windows-service-capable executable. Its present pipe is LocalSystem-plus-one-user,
local-computer-only, one-client, bounded, and HMAC authenticated. It intentionally
offers status only: stable device association and signed-client verification remain
mandatory before any filter-control command exists. The service has not been
installed or started.

Microsoft requires matching SDK and WDK build numbers; QFE numbers may differ. The
current [WDK installation guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk)
also calls for Visual Studio's Desktop development with C++ and the relevant
Spectre-mitigated libraries.

Microsoft also publishes an official
[WDK NuGet path](https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget)
that makes build tools reproducible in source and CI, while still requiring Visual
Studio 2026 with the C++ workload and Spectre-mitigated libraries. Tappy now uses
that path and keeps the native packages out of source control.

## Development signing

Development binaries may run only on a disposable test machine or VM configured for
driver testing. They use a locally generated test certificate and a visibly marked
test package. Tappy will not enable development-exclusive mode on a normal production
boot and will never change BCDEdit, Secure Boot, Memory Integrity, or signature
enforcement automatically.

Microsoft's [test-signing guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)
requires administrator action and a reboot. BattlEye explicitly states that its
protected games do not support Windows test-signing mode, another reason never to use
the owner's everyday gaming installation as the driver lab.

## Production signing policy

Tappy adopts a stricter gate than the minimum technically available for some Windows
client scenarios: public exclusive-input builds must pass HLK and return from the
Hardware Dev Center with a Microsoft signature. Attestation signing is reserved for
controlled testing and is not sufficient for a Tappy production release.

The release sequence is:

1. Build Release x64 with the current supported Visual Studio, WDK, SDK, and
   Spectre-mitigated libraries. Treat every compiler, Code Analysis, and Static Driver
   Verifier finding as a release blocker.
2. Create the INF and catalog with Inf2Cat. The package contains only the reviewed
   SYS, INF, CAT, broker, installer metadata, notices, and recovery documentation.
3. Run BinSkim and SignTool verification, dependency/SBOM generation, IOCTL fuzzing,
   Driver Verifier, PnP/power/CHAOS, upgrade/rollback/uninstall, and the complete
   fail-open matrix.
4. Test every path with Memory Integrity/HVCI enabled and pass the HLK HyperVisor Code
   Integrity Readiness Test. Microsoft's
   [HVCI compatibility guidance](https://learn.microsoft.com/en-us/windows-hardware/test/hlk/testref/driver-compatibility-with-device-guard)
   requires NX memory, no writable-plus-executable pages, no dynamic kernel code, and
   Driver Verifier/HLK validation.
5. Obtain an organization EV code-signing certificate, register it with the Microsoft
   Hardware Dev Center account, and sign the submission with SHA-256. Microsoft's
   [current signing requirements](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs)
   require an EV certificate associated with the dashboard account for attestation or
   WHCP submissions.
6. Submit the HLK package through the Windows Hardware Compatibility Program, download
   the Microsoft-signed result, and validate its signature and EKU before packaging.
7. Re-run install, runtime, recovery, Windows Update, security-feature, and anti-cheat
   tests against the exact returned bits. Hash every file and publish the hashes with
   the versioned release notes.

Microsoft describes HLK-tested dashboard signing as the recommended production path;
attestation is a testing scenario and cannot publish a retail driver through Windows
Update. See [driver signing options](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings).

## Required security evidence

- threat model reviewed by someone other than the implementation author;
- no arbitrary read/write IOCTL, user pointers, process memory, injection, hooks into
  protected processes, or dynamically executable kernel content;
- device-interface ACL admits only SYSTEM and the installed broker service identity;
- every IOCTL validates version, exact size, enum ranges, generation, authentication
  state, and bounded counts before changing policy;
- ring overflow, stale heartbeat, invalid packet, broker crash, app crash, and service
  kill all produce kernel-local pass-through;
- Driver Verifier, HVCI, fuzz, PnP, sleep/resume, unplug/replug, upgrade, rollback,
  uninstall, and Safe Mode recovery results are retained by version;
- standard Tappy remains completely usable in pass-through when the optional package
  is absent, disabled, rejected, or uninstalled.

Microsoft's [driver security checklist](https://learn.microsoft.com/en-us/windows-hardware/drivers/driversecurity/driver-security-checklist)
is the minimum checklist, not the whole Tappy release argument.

## Owner actions that cannot be automated

Production signing ultimately requires TerkWerX organization and payment decisions:
an EV certificate, a Microsoft Partner Center/Hardware Dev Center account associated
with that certificate, legal publisher details, and access to a dedicated HLK test
environment. No private key or Partner Center credential belongs in this repository,
CI log, support report, or chat.
