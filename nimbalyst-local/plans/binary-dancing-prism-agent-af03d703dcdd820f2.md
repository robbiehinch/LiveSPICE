# LTspice `.asc` File Format — Reference for Importer

Compiled primarily from KiCad's LTspice importer developer documentation (the most thorough public spec), cross-checked against LTwiki and the LTspice user manual / EngineerZone Q&A. The format is not officially documented by Analog Devices; KiCad's importer source is the de-facto reference.

Sources:
- [KiCad Developer Docs — LTspice import format](https://dev-docs.kicad.org/en/import-formats/ltspice/index.html)
- [EngineerZone — Syntax definition of .asy and .asc files](https://ez.analog.com/design-tools-and-calculators/ltspice/f/q-a/596907/syntax-definition-of-asy-and-asc-files)
- [LTwiki — Components Library and Circuits](https://ltwiki.org/?title=Components_Library_and_Circuits)
- [LTspice IV Library API (LTwiki PDF)](https://ltwiki.org/images/4/4e/LTSpicePartListAPI.pdf)
- [lt2circuitikz reference parser](https://github.com/ckuhlmann/lt2circuitikz)

---

## 1. File Structure

Plain ASCII, line-oriented, one record per line. Line endings may be LF or CRLF (strip trailing `\r`). Each line starts with an UPPERCASE keyword followed by space-separated fields. Tokens are whitespace-separated; the *last* field of `SYMATTR`, `TEXT`, and `WINDOW` value/text can contain spaces and runs to end-of-line.

**Required header (in this order, exactly two lines):**

```
Version 4
SHEET 1 <width> <height>
```

- `Version` (note capitalization — only the keyword `Version` is mixed case in practice; parsers should treat keywords case-insensitively to be safe). Common values: `4` (LTspice IV / XVII / 24).
- `SHEET <n> <w> <h>` — sheet index (almost always `1`), and drawing-area width/height in LTspice coordinate units.

**Body:** an unordered sequence of element records *with one structural rule*: `WINDOW` and `SYMATTR` lines belong to the most recently declared `SYMBOL` and must follow it (until the next `SYMBOL` or end-of-file). Everything else (`WIRE`, `FLAG`, `IOPIN`, `TEXT`, `LINE`, `RECTANGLE`, `CIRCLE`, `ARC`, `BUSTAP`, `DATAFLAG`) can appear in any order at the top level.

---

## 2. Record Reference

### Connectivity

| Record | Syntax | Notes |
|---|---|---|
| `WIRE` | `WIRE x1 y1 x2 y2` | Orthogonal segment (LTspice enforces H or V). |
| `FLAG` | `FLAG x y <netname>` | Net label at a wire endpoint. `FLAG x y 0` is the GND symbol — net `0` per SPICE convention. Any other name creates a global net label. |
| `IOPIN` | `IOPIN x y <direction>` | Hierarchical port marker. Direction: `In`, `Out`, `BiDir` (LTspice writes the full words; KiCad importer also accepts `I/O/B`). Must coincide with a `FLAG` at the same coordinate — the flag supplies the port name. |
| `BUSTAP` | `BUSTAP x1 y1 x2 y2` | Bus connection stub. Wires starting at the bus-side endpoint are promoted to bus rendering. |
| `DATAFLAG` | `DATAFLAG x y <expression>` | Probe annotation (".op" data tag). Typically ignored by importers. |

### Symbols and their attributes

| Record | Syntax | Notes |
|---|---|---|
| `SYMBOL` | `SYMBOL <libname> x y <orientation>` | `libname` is the .asy basename (no extension), with optional subdirectory using `\` or `/` (e.g. `Opamps\\opamp2`). Treat `\` and `/` as equivalent. `(x,y)` is the symbol's origin in sheet coordinates. Orientation: see §4. |
| `WINDOW` | `WINDOW <id> x y <justification> <size>` | Overrides position/justification/size of the attribute display whose ID is `<id>`. `(x,y)` is in *sheet* coordinates (not symbol-local) when in .asc; in .asy it's symbol-local. `size 0` hides the field in .asc. Must follow a `SYMBOL`. |
| `SYMATTR` | `SYMATTR <Key> <value...>` | Per-instance attribute. Key stored case-insensitive (canonical caps below); value is the remainder of the line and may contain spaces. `""` (two double-quote characters) means empty string. Must follow a `SYMBOL`. |

**Common WINDOW IDs** (KiCad importer maps only 0/3/38/39; other IDs exist but are mostly cosmetic):

| ID | Attribute shown |
|---|---|
| 0 | `InstName` (reference designator, e.g. `R1`) |
| 3 | `Value` |
| 38 | `SpiceModel` |
| 39 | `SpiceLine` |
| 123 / 124 | `Value2` / `SpiceLine2` (less consistently observed) |

**Common SYMATTR keys:**

| Key | Meaning |
|---|---|
| `InstName` | Instance reference (`R1`, `C3`, `U2`, ...). |
| `Value` | Primary value (`10k`, `1u`, `BC547`, `LM358`, ...). |
| `Value2` | Secondary value, often appended to model name. |
| `SpiceModel` | Model name when distinct from Value. |
| `SpiceLine` | Extra SPICE parameters appended after the value (e.g. `Rser=0.01`). |
| `SpiceLine2` | More parameters; concatenated after `SpiceLine`. |
| `Prefix` | SPICE device letter (`R`, `C`, `L`, `D`, `Q`, `M`, `J`, `V`, `I`, `E`, `F`, `G`, `H`, `B`, `T`, `X`, `K`). Absence in the .asy marks the symbol as a hierarchical sub-schematic. |
| `Description` | Free text. |
| `ModelFile` | Path to an external `.lib`/`.sub`/`.mod`. |
| `Type` | Device type (used for some primitives). |
| `ModelName` | Alternate model name field. |

### Free text & directives

`TEXT x y <justification> <size> <text...>`

- `<text>` is everything to end-of-line.
- Leading `!` → SPICE directive (prefix stripped after detection). Examples: `!.tran 10m`, `!.ac dec 100 1 1e6`, `!.model D1N4148 D(...)`.
- Leading `;` → comment.
- Within the text, the literal sequence `\n` and the escape sequences `! ` / `; ` are newlines (LTspice writes multi-line directives this way).
- `<size>` is an LTspice font-size code 1–7 (mapped to mils 36/42/50/60/72/88/108).

### Graphics-only (no electrical meaning)

| Record | Syntax |
|---|---|
| `LINE` | `LINE <Normal\|Wide> x1 y1 x2 y2 [style]` |
| `RECTANGLE` | `RECTANGLE <Normal\|Wide> x1 y1 x2 y2 [style]` |
| `CIRCLE` | `CIRCLE <Normal\|Wide> x1 y1 x2 y2 [style]` (defined by its bounding box) |
| `ARC` | `ARC <Normal\|Wide> x1 y1 x2 y2 sx sy ex ey [style]` — `(x1,y1)-(x2,y2)` is bounding box; `(sx,sy)`/`(ex,ey)` are start/end. **In .asc the start and end points are swapped vs .asy.** Counter-clockwise. |

`style`: 0 solid (default), 1 dash, 2 dot, 3 dash-dot, 4 dash-dot-dot.

### Justification codes (TEXT / WINDOW / PIN)

Horizontal: `Left`, `Center`, `Right`, `Top`, `Bottom`.
Vertical (text rotated 90°): `VLeft`, `VCenter`, `VRight`, `VTop`, `VBottom`.
Special: `Invisible` (hidden), `None` (PIN only — hide pin name).

---

## 3. Standard Symbol Library Names

These are the .asy basenames in `lib/sym/` of an LTspice install. Path separators in `.asc` are `\` (Windows) but `/` is equivalent. No extension is written.

**Passives (top-level):**
- `res`, `res2` — resistor (zig-zag, rectangle)
- `cap`, `cap2` — non-polar capacitor
- `polcap` — polarized electrolytic
- `ind`, `ind2` — inductor
- `xfmr` — coupled inductor / transformer
- `LED`, `LED2`, `varistor`, `varactor`

**Diodes / discretes:**
- `diode` (default), `schottky`, `zener`, `LED`, `TVSdiode`, `varactor`
- `npn`, `npn2`, `npn3`, `npn4`, `pnp`, `pnp2`, `pnp3`, `pnp4`
- `njf`, `pjf` — JFETs
- `nmos`, `nmos4`, `pmos`, `pmos4` — MOSFETs (`4` variants expose body terminal)
- `nigbt`, `pigbt` — IGBTs

**Sources:**
- `voltage`, `current` — independent V/I source (also serves as AC/PWL/SINE/PULSE etc. via `Value`/`Value2`)
- `bv`, `bi` — behavioral V/I source (`B` device)
- `e`, `f`, `g`, `h` — controlled sources (`E`/`F`/`G`/`H`)
- `signal`, `sine` — older convenience sources

**Misc primitives & connectivity:**
- `ttline`, `ltline`, `tline` — transmission lines
- `sw`, `csw` — voltage/current-controlled switch
- `ind3`, `load`, `load2`, `bv2`

**Sub-folders to recognize (search recursively):**
- `Opamps\opamp`, `Opamps\opamp2`, `Opamps\UniversalOpamp2`, `Opamps\AD8001`, ...
- `Comparators\` — `LT1011`, `LT1017`, ...
- `Digital\` — `and`, `or`, `nand`, `nor`, `xor`, `inv`, `buf`, `dflop`, `srflop`, `schmitt`, `schmtbuf`, `schmtinv`
- `Optos\`, `PowerProducts\`, `References\`, `FilterProducts\`, `SpecialFunctions\`, `Switches\`, `Misc\`

For an importer, mapping is by `Prefix` SYMATTR (from the .asy) rather than name when possible. If you don't have the .asy, you can hard-code a fallback table: `res*→R`, `cap*/polcap→C`, `ind*→L`, `diode/schottky/zener/LED→D`, `npn*/pnp*→Q`, `njf/pjf→J`, `nmos*/pmos*→M`, `voltage→V`, `current→I`, `bv/bi→B`, `e/f/g/h→E/F/G/H`, `sw/csw→S/W`, `Opamps\*` → `X` (subcircuit).

---

## 4. Coordinate System, Grid, Rotation, Mirror

- **Units** are dimensionless integers; KiCad's importer treats 16 LTspice units = 50 mils = 1.27 mm. The on-screen connection grid is 16 units (so wire endpoints, symbol origins, and pin coords are typically multiples of 16).
- **Y axis points DOWN** (screen convention). +X is right.
- A `SYMBOL`'s `(x,y)` is the *origin* of the symbol — the point that pins are defined relative to inside the .asy. The transform is applied about this origin.

**Orientation field** (single token at end of `SYMBOL` line):

| Code | Geometric transform applied to symbol's local coords before translating by (x,y) |
|---|---|
| `R0`   | identity |
| `R90`  | rotate 90° clockwise (on-screen): `(lx, ly) → (-ly, lx)` |
| `R180` | rotate 180°: `(lx, ly) → (-lx, -ly)` |
| `R270` | rotate 270° CW (= 90° CCW): `(lx, ly) → (ly, -lx)` |
| `M0`   | mirror about the local Y-axis (horizontal flip): `(lx, ly) → (-lx, ly)` |
| `M90`  | `M0` then `R90`: mirror-X then rotate 90° CW |
| `M180` | mirror about local X-axis: `(lx, ly) → (lx, -ly)` |
| `M270` | `M0` then `R270` |

(Equivalent: `Mθ` = mirror-X followed by `Rθ`. KiCad's table phrases it as "Mirror Y-axis then rotate" — same result since their Y is also screen-down.)

So `M0` versus `R0`: `R0` leaves the symbol as drawn; `M0` reflects it left-to-right around the symbol origin (pins on the left side end up on the right). The two are *not* the same in general because most LTspice symbols are not left-right symmetric (look at any transistor or diode).

---

## 5. Pin Positions — Defined in `.asy`, Not `.asc`

The `.asc` file contains **no pin coordinates**. To know where a wire connects to a symbol, you must read the symbol's `.asy` and transform its `PIN` records by the `SYMBOL` placement.

`.asy` records (superset of `.asc` records, minus electrical ones):

```
Version 4
SymbolType CELL                      # or BLOCK (for hierarchical / block diagram)
LINE ...   RECTANGLE ...   CIRCLE ...   ARC ...      # symbol body graphics
WINDOW <id> x y <just> <size>                        # default attribute display positions
SYMATTR Prefix R                                     # device-letter default
SYMATTR Value 1k                                     # default value
SYMATTR Description "Resistor"
PIN <lx> <ly> <pinjust> <nameoffset>
PINATTR PinName <name>
PINATTR SpiceOrder <n>
PIN <lx> <ly> <pinjust> <nameoffset>
PINATTR PinName <name>
PINATTR SpiceOrder <n>
```

- `PIN x y <justification> <nameoffset>` — `(x,y)` are symbol-local coordinates (origin at the `SYMBOL` placement point). `justification` is one of `LEFT`/`RIGHT`/`TOP`/`BOTTOM`/`NONE` (governs label side and visibility). `nameoffset` is the text offset in units; `0` typically hides the name.
- Each `PIN` is immediately followed by one or more `PINATTR` lines (`PinName` and `SpiceOrder` are the universal ones). `SpiceOrder` defines the order pins appear in the generated SPICE netlist line.

**Default pin offsets** for stock primitives (read off the shipped .asy files — sometimes useful as a fallback when you don't have the symbol library):

| Symbol | Pin 1 → Pin n (local x,y) | Notes |
|---|---|---|
| `res`   | (16, 0), (16, 80) | vertical, 80-unit body, grid-aligned |
| `cap`   | (16, 0), (16, 64) | vertical, 64-unit lead spacing |
| `polcap`| (16, 0), (16, 64) | + on pin 1 |
| `ind`   | (16, 0), (16, 80) | vertical |
| `diode` | (16, 0), (16, 64) | anode top (pin 1), cathode bottom (pin 2) |
| `voltage` | (0, 0), (0, 96) | + on pin 1 (top), - on pin 2 (bottom) |
| `current` | (0, 0), (0, 80) | arrow defines reference direction |
| `npn`/`pnp` | C(16,0), B(-16,32), E(16,64) | (collector, base, emitter) |
| `nmos`/`pmos` | D(16,0), G(-16,32), S(16,64) | 3-terminal symbol |
| `Opamps\opamp2` | In+(-32,32), In-(-32,96), Out(32,64), V+(0,32), V-(0,96) | 5-pin standard opamp |

These numbers can drift between LTspice versions — always parse the .asy if you have it.

A robust import flow: for each `SYMBOL`, locate the .asy (try `<libname>.asy` in user search paths, then in the LTspice install's `lib/sym/`), read its `PIN`s, apply the rotation/mirror transform from §4, then add the `SYMBOL`'s `(x,y)`. The resulting points are where `WIRE` endpoints will coincide.

---

## 6. Worked Example — RC Low-Pass

```
Version 4
SHEET 1 880 680
WIRE 144 96 144 80
WIRE 256 96 144 96
WIRE 144 192 144 176
WIRE 256 192 256 176
WIRE 256 192 144 192
WIRE 144 224 144 192
FLAG 144 224 0
SYMBOL voltage 144 80 R0
WINDOW 123 0 0 Left 0
WINDOW 39 0 0 Left 0
SYMATTR InstName V1
SYMATTR Value SINE(0 1 1k)
SYMBOL res 128 80 R0
SYMATTR InstName R1
SYMATTR Value 1k
SYMBOL cap 240 96 R0
SYMATTR InstName C1
SYMATTR Value 1u
TEXT 312 248 Left 2 !.tran 10m
```

Walk-through:

1. Header sets sheet size 880×680.
2. Five `WIRE` segments build the loop V1 (top) → R1 → node out → C1 → ground.
3. `FLAG 144 224 0` ties the bottom-of-cap node to net `0` (GND).
4. `SYMBOL voltage 144 80 R0` places V1 with its + pin at sheet `(144, 80)` (pin 1 of `voltage` is local (0,0)).
5. `WINDOW 123 0 0 Left 0` and `WINDOW 39 0 0 Left 0` hide the `Value2`/`SpiceLine` overlays.
6. `SYMATTR InstName V1` / `SYMATTR Value SINE(0 1 1k)` — the V1 source is a 1 kHz, 1 V sine.
7. `SYMBOL res 128 80 R0` puts R1 with local pin (16,0) at sheet (144, 80) and local pin (16,80) at sheet (144, 160) — the wire `WIRE 144 96 144 80` lands on R1's top pin (the small gap is the resistor body's drawn margin).
8. `SYMBOL cap 240 96 R0`: C1 with pin 1 at (256, 96), pin 2 at (256, 160).
9. `TEXT 312 248 Left 2 !.tran 10m` is the SPICE `.tran` directive (`!` prefix → directive, font size 2).

This is the minimum viable schematic LTspice will netlist as:

```
V1 N001 0 SINE(0 1 1k)
R1 N001 N002 1k
C1 N002 0 1u
.tran 10m
.backanno
.end
```

---

## Parser Implementation Notes

- Treat keywords case-insensitively but preserve the case of SYMATTR keys when round-tripping.
- After collecting all `FLAG` records, post-process: any `FLAG` whose coordinate also has an `IOPIN` becomes the port name, not a regular net label.
- Apply the rotation/mirror transform from §4 to compute absolute pin coords; then index wires by endpoint to build netlists.
- Be tolerant of unknown WINDOW IDs and unknown SYMATTR keys — pass them through.
- Be tolerant of extra fields and unknown records added by newer LTspice versions (skip unknown leading-keyword lines rather than aborting).
- LTspice writes integer coordinates always; rotation/mirror around the origin preserves the grid.
