# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository setup

The `ComputerAlgebra` directory is a git submodule and is required to build. After a non-recursive clone:

```
git submodule update --init --recursive
```

## Build / test / run

Solution: `LiveSPICE.sln`. The WPF app, the VST3 plugin, and `MockVst` are Windows-only (WPF, WinForms, ASIO, VST3 bridge). The cross-platform Avalonia head (`LiveSPICE.Avalonia/`) builds and runs on macOS and Windows; Linux has not been tested but is expected to work given Avalonia / Skia coverage.

All projects target .NET 10. Core libraries (`Circuit`, `ComputerAlgebra`, `Util`, `Audio`, `WaveAudio`, `Asio`, `CoreAudio`, `SchematicControls.Abstractions`, `SchematicControls.Editor`) are `net10.0`; the WPF app and the cross-platform `Tests` target multi-frame (`net10.0` cross-platform, `net10.0-windows` when the Windows-Forms plot tool is wanted); `LiveSPICEVst` and `MockVst` are `net10.0-windows`.

### Windows (full feature set)

```
cd LiveSPICE      && dotnet publish -c Release --framework net10.0-windows /p:DebugType=None /p:UseSharedCompilation=false /p:UseRazorBuildServer=false
cd LiveSPICEVst   && dotnet publish -c Release --framework net10.0-windows /p:DebugType=None /p:UseSharedCompilation=false /p:UseRazorBuildServer=false
```

### macOS (Avalonia head)

1. Install the .NET 10 SDK (`brew install dotnet` gives 10.0.x). `dotnet --list-sdks` should show a `10.0.*` entry. If the binary isn't on your path, `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"` and add `$DOTNET_ROOT/bin` to `$PATH`.
2. Build the miniaudio native binding once: `cd CoreAudio/Native/src && ./build.sh`. This produces `libminiaudio.dylib` under `CoreAudio/runtimes/osx-arm64/native/` (and the x64 sibling) which the CoreAudio managed binding P/Invokes into.
3. Build & run the Avalonia head:
   ```
   dotnet run -c Release --project LiveSPICE.Avalonia
   ```
   Or produce a standalone `LiveSPICE.app` bundle (self-contained, ~110 MB):
   ```
   cd LiveSPICE.Avalonia && ./macos/build-app.sh        # host arch, e.g. arm64
   open bin/Release/net10.0/osx-arm64/LiveSPICE.app
   ```

### Tests (cross-platform)

Tests are **not** xUnit/NUnit — `Tests/` is a `System.CommandLine` console app (`Tests/Program.cs`) that loads `.schx` schematics, simulates them, and checks results. Invoke it via `dotnet run`:

```
cd Tests
dotnet run -c Release --framework net10.0 -- test "Circuits/*.schx"
dotnet run -c Release --framework net10.0 -- test "Examples/*.schx"
dotnet run -c Release --framework net10.0 -- import-ltspice "LTSpice/*.asc"
```

Useful flags on `test`: `--plot` (Windows-only — requires the `net10.0-windows` framework), `--stats`, `--samples <N>`, `--sampleRate <Hz>`, `--oversample <N>`, `--iterations <N>`. To run a single circuit, pass its path as the glob (e.g. `test "Examples/Big Muff Pi.schx"`). The `Tests` app also has a `benchmark <pattern>` subcommand that reports analysis/solve time and realtime factor per circuit. For lower-level microbenchmarks there is a separate `Benchmarks/` project (BenchmarkDotNet).

The Windows-only Inno Setup installer (`LiveSPICESetup.iss`) is built with `iscc` after the WPF/VST `dotnet publish` steps above.

## Architecture

This is a real-time analog-circuit simulator: a user draws a schematic, and the engine generates a per-sample simulation function that runs on live audio.

The pipeline, top to bottom:

1. **Schematic (`Circuit/Schematic/`)** — `.schx` is the on-disk XML format. `Schematic` holds visual `Element`s (symbols + wires) and produces a `Circuit` via `Schematic.Build()`. LTSpice `.asc` files import through `Circuit/Schematic/Import/LTSpice/` into the same model.
2. **Circuit (`Circuit/Circuit.cs`, `Components/`)** — a netlist: `Component`s connected through `Terminal`s and `Node`s. `Circuit` itself derives from `Component` so subcircuits compose. Component models include passives, diodes (`Diodes.xml`), BJTs/JFETs (`Transistors.xml`), op-amps (`OpAmps.xml`), and vacuum tubes (`Tubes.xml` + `Components/VacuumTubes/`); the XML files are loaded at runtime and must be deployed alongside the binaries (the build copies them into `Components/`).
3. **Analysis (`Circuit/Analysis.cs`)** — builds Modified Nodal Analysis equations symbolically using the `ComputerAlgebra` submodule. Output is a system of equations + unknowns + initial conditions, with subcircuits namespaced via `Circuit.Prefix`.
4. **TransientSolution (`Circuit/Simulation/TransientSolution.cs`)** — symbolically solves the MNA system for a discretized timestep (linear vs. nonlinear partitioning, Newton iteration setup).
5. **Simulation (`Circuit/Simulation/Simulation.cs`)** — JIT-compiles the solution to a delegate via `ComputerAlgebra.LinqCompiler` (System.Linq.Expressions). The compiled `Process` method is what the audio thread calls per sample, with configurable `Oversample` and `Iterations`. Convergence failure throws `SimulationDiverged`.

Auxiliary projects:

- **`SchematicControls.Abstractions/`** — head-agnostic drawing primitives (`IDrawingContext`, `SymbolLayoutRenderer`, etc.) that let the same renderer paint schematics from WPF and Avalonia.
- **`SchematicControls.Editor/`** — head-agnostic editor tool model: `SchematicEditorCore`, edits, wire routing, pointer/input adapters; consumed by both UIs.
- **`SchematicControls/`** — WPF controls for the schematic editor; shared by the WPF app and the VST UI.
- **`LiveSPICE/`** — the WPF desktop application (`LiveSimulation.xaml` is where audio I/O meets the simulator). Windows-only.
- **`LiveSPICE.Avalonia/`** — the cross-platform Avalonia desktop application, with its own `Controls/`, `Windows/`, `Services/`, and an embedded MCP server (`Mcp/`) for Claude Code-driven schematic editing.
- **`LiveSPICEVst/`** — VST3 plugin built on `AudioPlugSharp`. Windows-only.
- **`MockVst/`** — standalone host that loads `LiveSPICEVst` for debugging without a DAW. Windows-only.
- **`Audio/`** — audio I/O abstractions (`Driver`, `Device`, `Stream`, `SampleBuffer`).
- **`WaveAudio/`** — Windows WaveOut backend. Skipped on non-Windows at runtime.
- **`Asio/`** — Windows ASIO backend. The Avalonia head only project-references it on Windows.
- **`CoreAudio/`** — macOS backend. Wraps `libminiaudio.dylib` via the thin C shim in `Native/src/livespice_miniaudio.c`. Only loaded at runtime on macOS.
- **`Util/`** — shared logging (`ILog`, `ConsoleLog`), reflection helpers, etc.
- **`Circuit/Spice/`** — SPICE netlist tokenizer/parser (component model import, not a full SPICE engine).

When changing component behavior, the simulator caches compiled `Process` delegates — anything that invalidates the model (e.g. setting `Solution`, `Oversample`, `Iterations`) calls `InvalidateProcess()` so the next sample recompiles. Honor that pattern when adding new tunable simulation state.
