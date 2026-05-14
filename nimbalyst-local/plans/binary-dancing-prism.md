# Plan: LTSpice `.asc` Importer for LiveSPICE (Phase 1)

## Context

The user wants to import LTSpice schematics into LiveSPICE so existing community circuits can be simulated by the LiveSPICE engine. Plain SPICE netlists are deferred because they lack geometry and would need a layout-synthesis pass. LTSpice's native `.asc` format already carries coordinates, rotations, and wire routing, so importing it is a translation job rather than a layout job.

Phase 1 goal: a working **File → Import LTSpice (.asc)…** path that handles a *defined whitelist* of common components, rejects everything else with a clear report, and produces a `.schx` schematic that LiveSPICE can simulate without manual cleanup for circuits using only whitelist parts.

## Scope

### In scope

- A pure-C# parser for the LTSpice `.asc` text format (Version 4), tolerant of unknown record types
- Symbol whitelist (RLC, common diodes, BJTs, JFETs, voltage/current sources, three opamp variants) — see table below
- Net connectivity via `WIRE` segments + `FLAG` (where `0` → Ground, named flag → `NamedWire`)
- Geometric translation from LTSpice's 16-unit grid to LiveSPICE's 10-unit grid, for all 8 LTSpice orientations (`R0`–`R270`, `M0`–`M270`)
- SPICE engineering-suffix value parsing (`1k`, `1u`, `100n`, `1Meg`, `1.5p`) — handled by reusing `Circuit.Quantity.Parse` (`Circuit\Utils\Quantity.cs:67`), which already understands SPICE prefixes
- Part-number lookup against `Circuit\Components\*.xml` (so `SYMATTR SpiceModel 1N4148` populates the right `IS`/`n`) — done by reusing the existing `LiveSPICE.Library` loader at `LiveSPICE\Controls\Library\Library.cs:117` via an injected interface
- An import report listing per-line warnings (unsupported symbols, unparseable values, fallback to defaults) shown to the user after import
- A "File → Import LTSpice (.asc)…" menu item in the WPF app

### Out of scope (will be reported as warnings and skipped)

- MOSFETs (`nmos`/`pmos`) — LiveSPICE has no MOSFET class
- Transformers (`xfmr`) — pin convention work; defer
- Behavioural sources (`bv`, `bi`, `e`, `f`, `g`, `h`) and dependent sources
- Hierarchical sub-schematics (symbols without a `Prefix` attribute)
- `TEXT` SPICE directives (`!.tran`, `!.model`, `!.lib`, `!.param`) — preserved as `Label` annotations only
- Digital, Comparators, Optos, References, Switches libraries
- `BUSTAP`, `IOPIN`, `DATAFLAG`
- Reading `.asy` files — pin offsets are hardcoded per whitelist entry from LTSpice's stock library
- Plain SPICE `.cir`/`.net` netlists (this is Phase 2)

### Component whitelist

| LTSpice symbol             | LiveSPICE class       | Mapping notes                                                          |
|----------------------------|-----------------------|------------------------------------------------------------------------|
| `res`, `res2`              | `Resistor`            | `Value` → `Resistance`                                                 |
| `cap`, `polcap`            | `Capacitor`           | `Value` → `Capacitance` (polarity ignored)                             |
| `ind`, `ind2`              | `Inductor`            | `Value` → `Inductance`                                                 |
| `diode`                    | `Diode`               | `SpiceModel`/`Value` → `PartNumber` if in `Diodes.xml`, else defaults  |
| `schottky`                 | `Diode`               | `Type=Diode` (no Schottky type in LiveSPICE)                           |
| `zener`                    | `Diode`               | `Type=Zener`                                                           |
| `LED`, `LED2`              | `Diode`               | `Type=LED`                                                             |
| `voltage`                  | `Rail` *or* `VoltageSource` | DC number → `Rail`; `SINE(...)`/expr → `VoltageSource`; PULSE/PWL/B → `VoltageSource` w/ warning |
| `current`                  | `CurrentSource`       | `Value` → `Current`                                                    |
| `npn`                      | `BipolarJunctionTransistor` | `Type=NPN`, `SpiceModel` → `PartNumber` lookup                     |
| `pnp`                      | `BipolarJunctionTransistor` | `Type=PNP`                                                          |
| `njf`                      | `JunctionFieldEffectTransistor` | `Type=N`                                                        |
| `pjf`                      | `JunctionFieldEffectTransistor` | `Type=P`                                                        |
| `Opamps\opamp`             | `OpAmp`               | `SpiceModel` → `PartNumber` lookup in `OpAmps.xml`                     |
| `Opamps\opamp2`            | `OpAmp`               |                                                                        |
| `Opamps\UniversalOpamp2`   | `OpAmp`               |                                                                        |
| `FLAG x y 0`               | `Ground`              | Placed at the flag coord                                               |
| `FLAG x y <name>`          | `NamedWire`           | `WireName = <name>`                                                    |
| `WIRE x1 y1 x2 y2`         | `Circuit.Wire`        | Coords scaled                                                          |

Anything else → recorded in the import report, then skipped. The schematic still loads; the user sees gaps where unsupported parts would have been.

## Approach

### Code layout

**New, in `Circuit\` (pure library, no WPF deps so it's reusable from MockVst / tests):**

- `Circuit\Schematic\Import\LTSpice\AsciiParser.cs` — token-based line parser, single forward pass, tolerant of unknowns.
- `Circuit\Schematic\Import\LTSpice\Ast.cs` — record types: `LTSpiceSheet`, `LTSymbol`, `LTWire`, `LTFlag`, `LTText`, plus the orientation enum.
- `Circuit\Schematic\Import\LTSpice\Whitelist.cs` — the symbol whitelist as data: for each entry, a `Func<LTSymbol, IPartLookup, Component>` factory, a pin-offset table in LiveSPICE local coordinates, and a `[from→to]` orientation map.
- `Circuit\Schematic\Import\LTSpice\LTSpiceImporter.cs` — orchestrator. Public surface: `static (Schematic, ImportReport) Import(string path, IPartLookup parts)` and `Import(TextReader, IPartLookup)`.
- `Circuit\Schematic\Import\LTSpice\IPartLookup.cs` — tiny interface (`Component TryGetByPartNumber(string)`) so the importer doesn't depend on `LiveSPICE.Library` directly.
- `Circuit\Schematic\Import\LTSpice\ImportReport.cs` — list of `(Severity, LineNumber, Message)` entries.

**WPF integration, in `LiveSPICE\`:**

- `LiveSPICE\Utils\Commands.cs` — add `public static readonly RoutedUICommand ImportLTSpice`.
- `LiveSPICE\MainWindow.xaml` — add menu item under File.
- `LiveSPICE\MainWindow.xaml.cs` — handler that:
  1. Shows an `OpenFileDialog` filtered to `.asc`.
  2. Wraps the existing `ComponentLibrary` as `IPartLookup` (small adapter).
  3. Calls `LTSpiceImporter.Import(path, lookup)`.
  4. If the report has entries, shows a simple text-window dialog with the warnings before opening the schematic.
  5. Creates a new untitled `SchematicEditor` tab populated from the returned `Schematic` (same code path that `New_Executed` uses, but with the imported schematic pre-populated rather than empty — confirm exact pattern during implementation).

### Parser shape

- Single pass over lines. Skip blank lines and lines starting with `#` (comments are not part of `.asc` but be defensive).
- Switch on the uppercase first token. For `SYMATTR`, `TEXT`, `WINDOW` the value field runs to end-of-line; for `SYMBOL` and `WIRE` the fields are whitespace-separated.
- `SYMATTR` and `WINDOW` records following a `SYMBOL` attach to that symbol. (Matches the rule from the format spec: those records belong to the most recent `SYMBOL`.)
- Unknown keywords → emit a warning to the report, continue parsing.

### Coordinate translation

- **Scale**: multiply LTSpice coordinates by `10/16` (= 0.625) and snap to the nearest 10. LTSpice enforces 16-unit grid alignment on connection points, so this is lossless for connectivity even with rounding. Document this assumption; emit a warning if a `WIRE` endpoint falls off the 16-grid.
- **Rotation/mirror**: 8-entry lookup mapping each LTSpice orientation to a `(int rotation, bool flip)` pair for LiveSPICE. The mapping is *derived empirically* per whitelist symbol — verified by the orientation-grid test (see Verification) — because LiveSPICE's `Symbol.MapToGlobal` has a non-obvious built-in Y-flip (`int y = flip ? Local.y : -Local.y;` at `Circuit\Schematic\Symbol.cs:54`).
- **Pin offsets**: for each whitelist symbol, the importer stores the pin coordinates in the **LiveSPICE local frame** (the same frame each `Component.LayoutSymbol()` uses), not in LTSpice's `.asy` frame. Generated wires get their endpoints from those offsets passed through `MapToGlobal`, which guarantees the importer's wire endpoints land exactly on the same coordinates LiveSPICE will compute when it draws the schematic. This avoids "wire visually touches the pin but doesn't electrically connect" bugs.

### Voltage-source decision rule

- `SYMATTR Value` is a plain DC number (e.g. `5`, `1.5`, `-9V`) → `Rail` with that voltage.
- `SYMATTR Value` is a function call (`SINE(...)`, `PULSE(...)`, `PWL(...)`, `AC ...`, `EXP(...)`, behavioural expression) → `VoltageSource` with a best-effort symbolic expression (`SINE(0 1 1k)` → `sin(1k*2π*t)`).
- Anything else → `VoltageSource` with the raw expression preserved as a `Label`, plus an import-report warning telling the user to review it.
- Never auto-map to `Input` — that's a deliberate choice the user makes when wiring up audio I/O. The report mentions this so the user knows they may want to swap a `Rail` or `VoltageSource` for an `Input`.

### Part-number lookup

- `IPartLookup.TryGetByPartNumber(string)` — case-insensitive. The WPF app's adapter walks `MainWindow.Components` (a `ComponentLibrary`) and indexes whatever it's already loaded.
- For tests, a small fixture builds an in-memory lookup by loading the same XML files directly via `XDocument` (no WPF dependency).

## Files to create / modify

**New (`Circuit\`):**

- `Circuit\Schematic\Import\LTSpice\AsciiParser.cs`
- `Circuit\Schematic\Import\LTSpice\Ast.cs`
- `Circuit\Schematic\Import\LTSpice\Whitelist.cs`
- `Circuit\Schematic\Import\LTSpice\LTSpiceImporter.cs`
- `Circuit\Schematic\Import\LTSpice\IPartLookup.cs`
- `Circuit\Schematic\Import\LTSpice\ImportReport.cs`

**New (`Tests\`):**

- `Tests\LTSpice\rc_lowpass.asc`
- `Tests\LTSpice\diode_clipper.asc`
- `Tests\LTSpice\common_emitter.asc`
- `Tests\LTSpice\non_inverting_opamp.asc`
- `Tests\LTSpice\orientation_grid.asc`
- A new `import-ltspice` subcommand verb on the `Tests` executable that runs the importer against each `.asc` and asserts the resulting `Schematic.Build()` succeeds. Matches the existing pattern (`Tests` is a `System.CommandLine` executable, not xUnit — see `CLAUDE.md`).

**Modified (`LiveSPICE\`):**

- `LiveSPICE\Utils\Commands.cs` — add `ImportLTSpice` command
- `LiveSPICE\MainWindow.xaml` — File menu item + binding
- `LiveSPICE\MainWindow.xaml.cs` — handler + small `ComponentLibrary` → `IPartLookup` adapter

## Reused existing code

- `Circuit.Quantity.Parse` (`Circuit\Utils\Quantity.cs:67`) — already handles SPICE-style engineering prefixes via its `Prefixes` table; the importer hands it values like `"1k"` with the unit-aware overload and reads back a `Quantity`. Note: need to confirm "Meg" / "MEG" specifically during implementation since SPICE distinguishes `M` (milli) from `Meg` (mega); if `Quantity.Parse` doesn't do this disambiguation, normalise the string in the parser before passing it on.
- `LiveSPICE.Library.LoadLibrary` (`LiveSPICE\Controls\Library\Library.cs:117`) — the part-number index already exists; the adapter just delegates to it.
- `Circuit.Schematic`, `Circuit.Schematic.Symbol`, `Circuit.Schematic.Wire` — constructed and added to a `Schematic` programmatically; existing serialization (`Component.Serialize` at `Circuit\Component.cs:122`, `Symbol.Serialize` at `Circuit\Schematic\Symbol.cs:132`) round-trips the result to `.schx`.

## Verification

### Automated (via the `Tests` exe)

For each fixture `.asc`, the new `import-ltspice` verb:

1. Runs the importer, asserts the `ImportReport` has the expected warnings (or none, for clean fixtures).
2. Saves the resulting `Schematic` to a temp `.schx`.
3. Loads it back via the normal `Schematic.Load` path to confirm round-trip.
4. Calls `Schematic.Build()` (or the equivalent — confirm the test runner's existing invocation in `Tests\Program.cs` during implementation) to confirm the circuit can compile.

Fixtures:

- `rc_lowpass.asc` — V-source + R + C + ground. Smoke test.
- `diode_clipper.asc` — input + two diodes back-to-back + output. Confirms diode lookup + symmetric placement.
- `common_emitter.asc` — NPN BJT, biasing R + C, rails. Confirms 3-terminal mapping.
- `non_inverting_opamp.asc` — opamp + feedback network + dual rails. Confirms 5-pin opamp + part-number lookup.
- `orientation_grid.asc` — 8 copies of a diode in all 8 LTSpice orientations, each with its own wires to ground. The test asserts that every diode's pin coordinates after `MapToGlobal` match the wire endpoints the importer generated. This is the test that catches off-by-one mistakes in the rotation/flip table.

### Manual end-to-end

- Build & run LiveSPICE: `dotnet run -c Release --framework net10.0-windows` from `LiveSPICE\`
- File → Import LTSpice → pick `Tests\LTSpice\rc_lowpass.asc` → confirm a 4-symbol schematic opens with all wires connected and no red dangling-pin indicators
- Repeat with `diode_clipper.asc`; route a `Input` to the source and a `Speaker` to the output and confirm Simulate produces audio output
- Pick a real LTSpice schematic from the LTSpice user community (e.g. a Bassman preamp `.asc`) and confirm either it imports cleanly or the report clearly lists the unsupported parts

## Open questions to resolve during implementation (none block the plan)

- Confirm whether `Quantity.Parse` distinguishes SPICE `M` (milli) from `Meg` (mega) — if not, normalise in the parser.
- Confirm the exact `Tests\Program.cs` invocation pattern for the existing `test` verb so the new `import-ltspice` verb matches.
- Confirm the cleanest way to inject a `Schematic` into a new `SchematicEditor` tab (vs the existing "open a file path" code path) — may require a tiny refactor of `SchematicEditor` or just writing the imported schematic to a temp `.schx` and reusing the existing open path. The latter is simpler if a refactor isn't trivial.
