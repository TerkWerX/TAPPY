# Akai Professional APC MINI v1 support boundary

> Status: code-supported with an attended physical MIDI-input spot check; finite all-control HIL pending

Tappy recognizes the exact Windows MIDI port name `APC MINI` as the original
APC MINI model. It does not apply this definition to APC mini mk2, APC Key,
APC20, APC40, APC64, or another similarly named product.

When the controller is confirmed, Tappy immediately publishes a fixed 99-control
assignment surface instead of waiting for each physical control to be learned:

- 64 pad controls, presented as eight rows of eight;
- eight clip-stop controls;
- eight scene-side controls and Shift; and
- increase and decrease assignment directions for each of the eight channel
  faders and the master fader.

The fixed grid remains separate from the product image. Selecting a square or
moving/pressing its physical source illuminates the matching non-interactive
hotspot on the image. Each fader has one physical hotspot shared by its increase
and decrease assignment squares.

The current input definition expects channel 1, pad notes `0`–`63`, clip-stop
notes `64`–`71`, scene-side notes `82`–`89`, Shift note `98`, and fader control
changes `48`–`56`. Those identifiers remain an attended hardware-validation
target: a successful screenshot spot check showed real APC MINI notes reaching
Tappy, but did not record every printed control against every MIDI identifier.
Any unexpected input remains visible to the session rather than being silently
discarded.

The original APC MINI's verified Ableton control script uses note velocities
`0` (off), `1` (green), `3` (red), and `5` (amber) for steady bi-color LED
feedback. Once this exact model is confirmed, Tappy therefore offers only
**Off/default**, **Green**, **Red**, and **Amber** in the square-color picker.
The **Sync supported LEDs** command sends those colors only to the confirmed
exact `APC MINI` output port. Colors saved by an earlier development build are
migrated to the equivalent supported color (or off when no equivalent exists),
so the on-screen result matches the hardware result. The Shift light and faders
are excluded. If no exact or unambiguous output port exists, Tappy keeps the
saved visual colors and sends nothing.

The runtime photo uses a tightly framed neutral-light derivative. Saved colors
are rendered by Tappy's hotspot layer rather than baked into the photograph;
the same color becomes fully illuminated during a matching physical press.
Faders and other non-color-addressable regions remain unchanged.

Akai's original [APC mini user guide](https://cdn.inmusicbrands.com/akai/apc-mini/APC%20mini%20-%20User%20Guide%20-%20v1.0.pdf_079659375431bb679d17071da25ad6af.pdf)
is the primary source for the physical 8×8 clip matrix, nine faders, clip-stop
buttons, scene-launch column, USB MIDI behavior, and Shift surface. Akai publishes
a separate [APC mini mk2 communication protocol](https://cdn.inmusicbrands.com/akai/attachments/APC%20mini%20mk2%20-%20Communication%20Protocol%20-%20v1.0.pdf),
but Tappy does not apply that newer model's protocol to this v1 definition.

## Required finite validation

1. Confirm the device while Rehearsal Mode is on and verify all 99 squares are
   present before another physical press.
2. Press every pad, clip-stop, scene-side, and Shift control once; confirm exactly
   one expected square and photo hotspot respond for each.
3. Move each fader upward and downward; confirm its matching direction square and
   shared photo hotspot respond without leaving a held state.
4. Test simultaneous pads, repeated taps, disconnect/reconnect, profile save/load,
   and input from an unselected controller.
5. Record any identifier mismatch before promoting the model to Functional.
