# Tappy project-start package

Tappy is an early-development TerkWerX sister application to Tippy. It turns dedicated
USB numpads, compact keyboards, gaming keypads, macro pads, and—when deliberately
selected—even a full keyboard into a programmable hand-operated control surface.

This folder is intentionally separate from `F:\TIPPY`. Starting Tappy must not
modify Tippy, reuse Tippy's application identity, or place build output inside a
website synchronization folder.

## Files in this package

- [`_PROMPT.md`](./_PROMPT.md) — the original bootstrap brief, retained as historical
  input; later owner decisions are recorded in the living decision and release docs.
- [`docs/PROJECT_TEMPLATE.md`](./docs/PROJECT_TEMPLATE.md) — the living product,
  architecture, safety, testing, and release specification.
- [`docs/TIPPY_REUSE_MAP.md`](./docs/TIPPY_REUSE_MAP.md) — a component-by-component
  guide to what should be reused, generalized, adapted, or replaced.
- [`docs/ASSET_INVENTORY.md`](./docs/ASSET_INVENTORY.md) — the protected source-image
  inventory, completed derivative handoff record, and rules for future use.
- [`docs/TESTING.md`](./docs/TESTING.md) — exact automated, package, descriptor, and
  physical-evidence boundaries.
- [`docs/LOGITECH_G13.md`](./docs/LOGITECH_G13.md) — exact G13 identity, implemented
  protocol boundary, public sources, and uncompleted HIL gate.

## Current bootstrap checkpoint

The local repository, architecture, decision record, test projects, Raw Input
keyboard slice, K15 ContainerId grouping, and dedicated G13 provider are established.
The current Release verification is 174 passing tests (Core 30, Windows 90, App 31,
G13 HIL tool 23).
The schema-3 descriptor probe observes the K15 and physical G13 as separate logical
controllers without capturing input. Physical control/HIL evidence remains pending;
keep `F:\TIPPY` read-only and consult the living docs before changing scope.

The prompt deliberately treats per-device keyboard suppression as a separate,
security-sensitive engineering decision. Device-specific Raw Input can identify a
keyboard, but it cannot by itself prevent that keyboard's original keystrokes from
reaching every other application. Tappy must never pretend otherwise.
