# Initial Tappy controller artwork inventory

The source images under `F:\TAPPY\PAD IMAGES` predate this project-start package.
The separate image-processing handoff is complete and reconciled below: all source
byte streams were preserved, two PNG originals were relocated under `originals`, and
eight transparent derivatives were produced. These remain raw/reference candidates,
not approved application assets. No first-milestone runtime registry or package uses
them.

| Initial source filename | Likely device/category | Initial status |
|---|---|---|
| `3Dconnexion SpacePilot PRO USB 3DX-600037.png` | 3D navigation/controller device with buttons | Advanced-provider candidate; protocol and image rights need review |
| `free wolf freewolf k15 39.webp` | Freewolf K15-style one-handed gaming keypad | Reference candidate; compare with the JPG and confirm exact model |
| `freewolf free wolf ziyo lang k15 39.jpg` | Freewolf/K15-style one-handed gaming keypad | Reference candidate; exact branding/model needs confirmation |
| `Microsoft X821504_001 product id 02010_486_0126212_00934.jpg` | Microsoft-branded hand controller/keypad candidate | Exact product identity and usable view need confirmation |
| `Microsoft X821504_001 product id 02010_486_0126212_00934(wood).jpg` | Alternate view/background of the same Microsoft candidate | Compare only; do not register both as distinct hardware |
| `RAZER_tartarus RZ07-0103_0100.png` | Razer Tartarus one-handed gaming keypad | Strong layout candidate; exact revision/protocol requires real-device evidence |
| `Targus PAUK10U.webp` | Targus PAUK10U USB numeric keypad | Strong standard-numpad candidate |
| `TargusmodelPAUK10U ITEM NO 10.jpg` | Alternate Targus PAUK10U source | Compare views and retain the better factual source |

## Asset rules

- Preserve every original filename and source file. Never overwrite or delete a raw
  reference while producing a derived asset.
- Record source URL/owner, license or permission status, model confidence, view,
  processing history, and reviewer approval before shipping an image.
- Remove background, cord, and plug only when doing so does not alter the controller.
  Preserve logos, legends, key count, geometry, textures, colors, proportions, and
  shadows that belong to the hardware. Do not invent missing sides or keys.
- Correct rotation and perspective conservatively. Never stretch or crop controls.
- Preview transparent derivatives against both light and dark application surfaces.
- Treat similar shells and alternate photographs as views of one device unless
  hardware evidence proves distinct models.
- Registry matching requires protocol/identity evidence. A good photograph does not
  make a controller “verified.”
- Use generic code-rendered layouts whenever image identity, rights, or quality is
  uncertain.
- Final application assets belong in a canonical tracked controller-library source,
  not mixed into `PAD IMAGES`. Build validation must catch missing references,
  unlicensed sources, or accidental raw-file packaging.

## 2026-09-02 processing handoff reconciliation

The separate image-processing pass is complete. This section records what changed;
it does **not** approve an image for runtime use. All eight source byte streams are
still present. The two original PNGs were moved under `PAD IMAGES/originals`, while
same-name processed derivatives now occupy their former top-level paths. The other
six source files remain at their original top-level paths. No first-milestone
controller registry or package references this directory.

Every processed controller derivative is a `1370 x 880` transparent ARGB PNG:

| Processed derivative | SHA-256 |
|---|---|
| `3Dconnexion SpacePilot PRO USB 3DX-600037.png` | `75C9AC881254F592F05A7C1E2E681FB044115016EA1C8CFF47518B5A10566701` |
| `free wolf freewolf k15 39.png` | `D0A16589F9F0A9787703EEA50AC23166A5A3FE708595810708CD0D93201FCC39` |
| `freewolf free wolf ziyo lang k15 39.png` | `F515FFA1FE482782E3B789B122D15DFB5EA3DD727E39725FE93026B7167328A6` |
| `Microsoft X821504_001 product id 02010_486_0126212_00934.png` | `5CB5EC0A940C33C7570DB68F3D405DDF49012BE41C32620AD03D4697213FE767` |
| `Microsoft X821504_001 product id 02010_486_0126212_00934(wood).png` | `8D0967B9BA09650EB93F7DC063A4695695C27C1695F87E631B58E72B536A0519` |
| `RAZER_tartarus RZ07-0103_0100.png` | `E6E99B9A512D0CBDA5F724EC49F434B343DCAF5EE1B5BB56EE4CB0090DA1DBF5` |
| `Targus PAUK10U.png` | `AB94186FE344DEDCECF6F546FCB804E9E2F38FBDF2618758227FEFEF70604148` |
| `TargusmodelPAUK10U ITEM NO 10.png` | `FD324FAEF9D3E033DC93FD462B55E80709264CEFF8CBD54CD60EDCA0B238B874` |

Protected PNG sources now under `PAD IMAGES/originals` retain these hashes:

- `3Dconnexion SpacePilot PRO USB 3DX-600037.png` —
  `C55F9DF861833DB977868DCD8DCA62A4254B04E4109F4312DD0A806B53271E03`
- `RAZER_tartarus RZ07-0103_0100.png` —
  `5A87D5D3AB598AF92C283E848C21BB90BF9BFCDB31876C2D1FCB0328E9A553DC`

`PAD IMAGES/originals/tappy_pads_preview_light_dark.jpg` is a `1380 x 3840`
review sheet with SHA-256
`092862959E12838BEA3F7EFEFED540E64B1AB47C42C6B520DDF044C9F48E52B2`.
Despite its current folder, it is derived review material rather than an original.

Before any derivative can ship, record its source URL/owner, usage rights, exact
model confidence, protocol evidence, processing history, and human approval. Until
then, Tappy uses generic code-rendered layouts and `.gitignore` excludes the entire
`PAD IMAGES` tree from source control, builds, and portable artifacts.

## 2026-09-03 owner-supplied Logitech G13 locator

The product owner photographed the attached physical G13 and supplied the original
directly for the requested in-app visual locator. This is the first narrowly
approved runtime controller image; it does not approve any other derivative.

- Protected original: `PAD IMAGES/originals/Logitech G13 user photo 20260903_124613.jpg`
  (ignored, preserved locally), SHA-256
  `25A49B84273170E3E2D8CFDC21F97B3B34DC47977DE9BC54E6F29FCE7BF10F30`.
- Runtime derivative: `src/Tappy.App/Assets/Controllers/logitech-g13-user-photo.png`,
  `853 x 1844` RGBA PNG, SHA-256
  `67F74F1A9F7BF295E46BF4FBCA357E185D010CFB6448AAAF39F9632233511D83`.
- Ownership/permission: owner-created and owner-submitted; explicitly requested for
  the Tappy controller-photo UI in this project.
- Processing: the built-in image editor removed the tabletop, surrounding objects,
  and loose cable while preserving the photographed controller, labels, geometry,
  and perspective. A first opaque-checkerboard output and a later opaque crop were
  rejected. The accepted derivative was mechanically verified as 32-bit ARGB with
  transparent corner pixels; WPF crops only its transparent canvas at render time.
- Runtime scope: embedded as an application resource and selected only for the exact
  `raw-hid-g13`, VID `046D`, PID `C21C` identity. The separate assignment grid remains
  interactive; 39 non-interactive hotspots mirror grid selection and physical input.
- Evidence boundary: visual alignment and software state mapping do not promote G13
  hardware support. Physical HIL and Controller Passport gates remain unchanged.

## 2026-09-02 Freewolf enumeration note

With a user-identified Freewolf K15 candidate attached, the sanitized descriptor
probe now reports one authoritative ContainerId group at VID `1A2C`/PID `2D43`,
containing four Raw Input keyboard interfaces with reported total-key capabilities
of 56 and 264. No key events were captured. The grouping is implemented and covered
by deterministic tests, but the observation does not prove physical key behavior,
exact photo-to-unit identity, image rights, or Functional/Verified support and does
not approve either Freewolf derivative for shipping.

The SpacePilot Pro image remains only a visual/provider candidate. Dedicated G13
support does not supply a SpacePilot implementation: the two devices can share Raw
Input transport, identity, selection, lifecycle, and cleanup concepts, but not a
report protocol or decoder.
