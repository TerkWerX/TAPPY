# Tappy website

A self-contained marketing site for Tappy, styled after the Tippy website in `F:\TIPPY\WEBSITE`. It uses Tappy's supplied tattooed-hand branding, a blue/mint/dark palette, responsive sections, and a small interactive controller concept.

## Open it

Double-click `index.html` in this folder. It opens the finished site in `dist/index.html`. All assets are local; no package install or build is needed.

For an HTTP preview, with Node.js installed:

```powershell
cd F:\TAPPY\WEBSITE
node .\preview.mjs
```

Open `http://127.0.0.1:4317/`. Press Ctrl+C in that terminal to stop the preview. The server is loopback-only, not exposed to the network. Set `TAPPY_SITE_PORT` if another preview already uses that port.

## Files to edit

- `dist/index.html`: page content, navigation, demo markup, FAQs.
- `dist/site.css`: visual styling and responsive layouts.
- `dist/site.js`: mobile navigation, three sample workflows, 36 example assignments, and photo highlighting.
- `dist/assets/`: supplied branding and controller photos, copied without image modification.
- `dist/favicon.ico`: existing Tappy hand application icon.
- `validate.mjs`: dependency-free static checks and in-memory demo checks.

The example workflows are website illustrations, not a downloadable preset pack or a screenshot of the real app. The demo does not request device access, send keys or MIDI, run commands, open files, or transmit OSC. No analytics, external fonts, trackers, or runtime dependencies are used.

## Validation

```powershell
node .\validate.mjs
node --check .\dist\site.js
node --check .\preview.mjs
```

These check local references, markup structure, selected contrast pairs, and all 36 demo selections using an in-memory document stub. They are not browser rendering, assistive-technology, or physical-device tests. Responsive breakpoints, focus treatment, reduced-motion support, and native expandable FAQs are included.

## Publishing to the existing host

The public site is **https://www.terkwerx.com/tappy/**, in the owner's existing
cPanel `public_html/tappy` directory. It was first published September 10, 2026.
The September 21 batch-sync request includes this website in the public Tappy
repository, alongside the app source. Generated upload ZIPs and local hosting
metadata remain ignored.

Edit `dist` first, run the validation checks, and preview the result in a browser.
Back up the existing local mirror, then copy the contents of `dist` into
`J:\TerkWerX page\tappy`. Only that subdirectory is in scope; do not replace the
main TerkWerX site or sibling projects. Back up the host's current Tappy directory
outside the public web root before uploading. Package the **contents** of `dist`,
not its containing folder, and extract into `public_html/tappy`. Verify the live
page and supporting assets against the local files after deployment.

The hosting provider may append its own `secureserver.net` / `wsimg.com`
performance-monitoring scripts after the page body. These are not part of the
site source; do not change host-wide settings during a Tappy upload.

**September 21, 2026 deployment:** the local mirror was synced before upload and
all 13 public files were verified against `dist`. The 12 supporting files match
byte-for-byte; the HTML matches after excluding only the provider's existing
performance-script insertion. A desktop preview and narrow-screen overflow check
were performed; the latter caught and corrected a small hero-background overflow.
The previous host directory is preserved as `tappy-before-sync-20260921` in the
account home outside `public_html`. The previous local mirror and upload bundle
are under ignored `artifacts/site-sync-20260921` in the Tappy workspace.

Relative asset paths work at the domain root or in a `/tappy/` subdirectory. The
canonical URL points to the public Tappy site. `.openai/hosting.json` identifies
`dist` as static output but has no hosting project ID and does not publish anything;
this site uses cPanel, not Sites hosting. Recreate any upload ZIP after edits.

The site remains a development showcase, not a signed-installer download. Review
release/licensing wording and desktop/mobile accessibility before each publication.
Update the development call to action only when a public release is actually
available. Do not substitute a local testing ZIP for a public installer.

## Reference and asset provenance

- Visual reference: local Tippy site, including its charcoal backgrounds, oversized headings, mint/blue accents, mascot placement, card rhythm, workflow section, and sister-project treatment. No hosting identity or app source was copied.
- `tappy-hand.png`, `tappy-hand-t.png`, `tappy-wordmark.png`: `F:\TAPPY\docs\images\TAPPY_hand.png`, `TAPPY_hand_T.png`, and `TAPPY_logo.png` supplied by the project owner.
- `logitech-g13.png`, `apc-mini.png`: existing neutral controller artwork from `F:\TAPPY\src\Tappy.App\Assets\Controllers`.
- `akai-fire.png`, `razer-tartarus.png`, `microsoft-number-pad.png`: approved application assets from that same controller directory, copied without modification. The owner explicitly approved these and the Targus photos for public GitHub/website use on September 21, 2026; see `docs/ASSET_INVENTORY.md`.
- G13 highlight coordinates: existing `ControllerPhotoCatalog.cs`, transformed into the same 1174 × 1339 photo space.
- `tippy-foot.png`: `F:\TIPPY\WEBSITE\deploy\images\tippy-toes-tippy.png`.
- `favicon.ico`: `F:\TAPPY\src\Tappy.App\Assets\Icons\tappy.ico`.

Device manufacturer and product names identify the pictured hardware; no manufacturer endorsement or universal compatibility is implied. Capability copy was checked against the local Tappy source and development records on September 21, 2026, including the Fire OLED, lighting animations, controller combinations, cross-controller banks, and layout updates. Implemented capabilities are distinguished from remaining hardware verification and driver-certification work.
