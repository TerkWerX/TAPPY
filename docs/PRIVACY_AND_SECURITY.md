# Privacy and security

Tappy is local-only by default: no account, cloud dependency, analytics, automatic
telemetry, hidden device inventory, or background upload.

## Controller-data boundary

Windows must deliver Raw Input broadly enough to enumerate keyboard-class devices,
and the model-specific G13 provider must receive its vendor-HID collection, but
Tappy discards events unless their session handle is the one the user is actively
identifying or has explicitly confirmed. Unselected input is not converted to text,
added to control state, published to WPF, counted per control, or retained.

For a selected controller, the implemented ordinary diagnostics retain only
aggregate press, release, repeat, current-held, and disconnect counts per sanitized
persistent controller identifier. Timing percentiles, rollover summaries, and
synthetic-release totals are future diagnostics, not version 0.1.0 claims. A
chronological scan-code list can reconstruct typing, so any future capture may exist
only during a short visibly armed session, kept in memory, bounded, and omitted from
standard support output. That capture is not part of version 0.1.0. Tappy never uses
keyboard-layout APIs to translate observed input into typed characters.

The G13 provider validates each vendor-HID report at the Windows boundary and emits
only normalized control transitions plus provider-local X/Y state. Ordinary
diagnostics and profiles do not persist or serialize raw G13 reports, analog samples,
or a chronological control stream. Tappy sends no G13 output reports.

## Identity and reports

Raw device paths remain inside the Windows provider. Persistent profiles contain a
one-way instance fingerprint, sanitized VID/PID/usage metadata, confidence, and the
user's alias—not the raw path. Standard support/debug output excludes:

- user and computer names;
- raw or reversible device paths;
- chronological key codes and typed text;
- profiles, mappings, macro/text bodies, variables, clipboard, arguments, and
  PowerShell content;
- documents, window content, browser history, and application data;
- tokens, credentials, private keys, and environment secrets.

Version 0.1.0 has aggregate local diagnostics and no upload or telemetry path. Any
future report must be written locally for review and never submitted automatically.

The schema-3 descriptor probe performs enumeration only: it registers no input and
opens no reports. The separate G13 input verifier performs no enumeration or capture
without the exact `--arm` flag, stops after a finite timeout, and writes only an
aggregate local result under a random run directory. It excludes raw reports,
device paths, ContainerIds, control chronology, typed text, mappings, and profiles.
No armed G13 physical run has completed; verifier tests establish the privacy/refusal
contract, not hardware behavior.

## Output and recursion safety

Every keyboard output is owned by an execution lease and injected with Tappy's
nonzero marker. Matching/device-less input is rejected. Shared output keys use
reference counts so one controller cannot release another controller's still-held
modifier. Depth and output-rate limits contain cycles such as `A -> B`, `B -> A`.
Emergency stop, unplug, source/backend failure, lock, suspend, profile swap, and an
orderly exit cancel work and release owned outputs. A previous-session marker warns
at the next launch after an unclean exit; a hard process or OS failure cannot execute
in-process cleanup and is not claimed as a successful release path.

SendInput neither elevates nor bypasses Windows policy. It may fail in elevated,
secure-desktop, exclusive-input, remote-session, accessibility, or anti-cheat
contexts. Tappy does not weaken AppLocker, WDAC, Constrained Language Mode, execution
policy, or application/game rules.

## Imports and future active content

Profiles, portable layers, and controller packs are parsed to an inert preview.
Program launch, PowerShell, OSC/network destinations, and similar active steps stay
disabled until reviewed. Future support packs accept declared bounded JSON/PNG/CSV
data only, reject traversal/symlinks/duplicates/unknown types/oversize and archive
bombs, verify per-file SHA-256, require a trusted publisher signature for catalog
delivery, and reject downgrades. No pack may introduce executable code.

## Exclusive input

No filter driver, global keyboard hook, HidHide, Interception, or comparable system
component is installed or enabled. True per-device suppression is a separate future
design requiring a valid signed fail-open driver or vendor mechanism, administrator
consent, recovery instructions, legal/security review, and anti-cheat compatibility
review. It cannot be silently added as an implementation detail.

## Vulnerability posture for 0.1.0

The local bootstrap is not a security boundary against a malicious local user. The
milestone's security purpose is to avoid accidental observation, identity leakage,
input storms, stranded output, unsafe imports, and misleading suppression claims.
Public threat modeling, dependency review, signing, and disclosure policy are release
gates, not completed claims.
