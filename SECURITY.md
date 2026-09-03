# Security policy

## Supported versions

Tappy is currently an early-development `0.1.x` bootstrap, not a public production
release. Security fixes are made on the latest `main` branch. No older version is
currently supported, and unsigned local builds should not be treated as trusted
distribution artifacts.

## Report a vulnerability privately

Use GitHub's private vulnerability reporting for this repository:

https://github.com/TerkWerX/TAPPY/security/advisories/new

If that private form is unavailable, do not open a public issue with sensitive
details. Contact the TerkWerX repository owner through a private, independently
verified channel and ask for a private reporting path. Public issues are appropriate
only after the maintainer confirms disclosure is safe.

Include the affected Tappy version or commit, impact, prerequisites, and minimal
reproduction steps. Use synthetic controller input and harmless F13–F24 output when
possible. Do not send executable proof-of-concept files unless a maintainer requests
one through the private advisory.

## Protect input and device data

Even in a private report, remove unrelated or unnecessary data. Never include:

- access tokens, credentials, cookies, private keys, or other secrets;
- typed text, clipboard content, chronological scan/key histories, or memory dumps;
- profiles, mappings, macros, scripts, arguments, documents, or application data;
- raw or reversible HID/USB device paths, serial numbers, usernames, or computer
  names; or
- unreviewed logs, screenshots, crash exports, support reports, or device inventories.

Describe controls by printed label or Tappy control ID. Share only the minimum
sanitized descriptor and synthetic fixture needed to reproduce the issue. Tappy
reports are local and must always be reviewed before attachment; the project will
never ask for a password, API key, or complete device inventory.

## Security boundaries

Tappy's current Device-aware pass-through mode does not suppress the controller's
ordinary Windows keystroke. `SendInput` does not elevate privileges or bypass the
secure desktop, application isolation, anti-cheat systems, AppLocker, WDAC, or
Windows policy. Reports that depend on exclusive capture or bypassing those controls
are outside the current security model.

High-priority reports include unintended observation of unselected keyboards,
disclosure of raw device identity or captured input, recursion/output storms,
stranded held keys, unsafe profile or pack parsing, path traversal, signature/trust
bypass, and package contents outside the documented allowlist.

The project does not install a filter driver, keyboard hook, or suppression backend.
Any proposal that introduces one requires explicit user consent, fail-open recovery,
driver signing, Windows and anti-cheat review, and a separate threat model.

## Disclosure and response

Maintainers will acknowledge a private report when practical, reproduce it using
the least-sensitive data available, and coordinate a fix and disclosure timeline
based on impact. Please allow time for validation before publishing details. No bug
bounty or guaranteed response time is offered at this bootstrap stage.
