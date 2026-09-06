# Anti-cheat and protected-application compatibility

> Status: policy defined; no anti-cheat product or game is currently certified

## Default posture

Tappy is an accessibility/productivity controller mapper, not an anti-cheat bypass.
It will not hide its service or driver, tamper with another process, inspect protected
memory, inject code, spoof hardware, evade module checks, disable a security product,
or advise a user to weaken Windows security.

The presence of a Microsoft signature proves package identity and Windows trust; it
does not mean a game publisher permits macros, input remapping, or the driver. Every
game's rules still apply. Even pass-through Tappy output may be prohibited by a
particular title or competition.

## Runtime policy

Until an exact product/version has current positive evidence, starting a protected
application must result in this sequence:

1. stop accepting new mapped controller actions;
2. release every output owned by Tappy;
3. request `PassThrough` for every keyboard filter instance;
4. wait for driver acknowledgement and expose the result in the UI;
5. stop the optional broker session for the duration of protected play.

The filter's watchdog independently restores pass-through if any step cannot finish.
Process-name lists are not a security boundary and are easy to make stale, so the
release implementation must combine an explicit user-facing Protected Application
Mode with signed, versioned compatibility data. Automatic detection may improve the
experience but never converts “unknown” into “allowed.”

## Initial matrix

| Environment | Exclusive keyboard mode | Tappy mappings | Current claim |
|---|---|---|---|
| Ordinary desktop applications | Pending signed-driver validation | Available under existing safety rules | Pass-through supported; exclusive not shipped |
| Windows with Memory Integrity/HVCI | Required release test | Available if Windows accepts output | No driver claim until HVCI + HLK pass |
| Windows test-signing or kernel debugging | Refused outside isolated development | Not an anti-cheat test configuration | Lab only |
| BattlEye-protected title | Forced off unless future exact approval says otherwise | User/game rules control; default recommendation is off | Unverified |
| Easy Anti-Cheat-protected title | Forced off unless future exact approval says otherwise | User/game rules control; default recommendation is off | Unverified |
| Riot Vanguard-protected title | Forced off unless future exact approval says otherwise | User/game rules control; default recommendation is off | Unverified |
| Tournament/esports environment | Off | Off unless organizer explicitly approves | Unsupported without written organizer approval |

BattlEye's own [FAQ](https://www.battleye.com/support/faq/) says it may kick users for
specific macro tools, does not support Windows test-signing mode, and may block
hardware software whose kernel drivers contain known exploitable flaws. That supports
Tappy's conservative off-by-default policy; it is not a compatibility approval.

## Evidence required to change a matrix row

- exact anti-cheat product, game, client, Windows, Tappy, broker, and driver versions;
- current publisher/anti-cheat documentation or written vendor response;
- clean Microsoft-signed production package, with Secure Boot and HVCI left enabled;
- install, launch, gameplay, input, sleep/resume, update, and uninstall tests on a
  separate noncompetitive account/environment where the publisher permits testing;
- no kick, block, warning, crash, degraded security control, or unexpected input;
- rerun after any Tappy driver/broker change or anti-cheat/game update.

An absence of a ban or error is not approval. Tappy will say `Unverified`, `Blocked`,
or `Allowed for exact versions`; it will never advertise universal anti-cheat
compatibility.

## Reporting a conflict

Do not ask users for protected-game memory dumps, anti-cheat internals, credentials,
or broad device inventories. Collect the exact public versions, signed-file hashes,
Windows event/code-integrity error identifiers, Tappy's aggregate mode transitions,
and whether pass-through recovery succeeded. Follow the private process in
[`SECURITY.md`](../SECURITY.md) for anything that could expose a vulnerability.
