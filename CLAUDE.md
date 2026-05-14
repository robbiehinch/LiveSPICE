# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LiveSPICE is a real-time SPICE-like circuit simulator for guitar amps and effects. It comprises a WPF desktop application and a VST3 plugin that read audio in, simulate a user-defined circuit, and play the result. The simulation engine relies on the `ComputerAlgebra` submodule, which symbolically simplifies the circuit's equations and JIT-compiles them to LINQ-expression delegates at runtime — this is the core trick that makes user-authored circuits run at audio rates.

Project site: http://www.livespice.org

## Repository layout

`ComputerAlgebra/` is a **git submodule**. Always clone with `--recursive`, and run `git submodule update --init --recursive` if it appears empty. It has its own solution (`ComputerAlgebra/ComputerAlgebra.sln`) which additionally contains a native C++ demo (`LotkaVolterraCpp.vcxproj`); the main `LiveSPICE.sln` at the repo root references the managed `ComputerAlgebra.csproj` project directly.

Projects, in dependency order (this order also drives the in-flight .NET upgrade):

- **Foundation**: `ComputerAlgebra` (submodule), `Util`
- **Core libs**: `Circuit`, `Audio`, `WaveAudio`, `Asio`
- **Upper**: `SchematicControls` (WPF schematic editor control), `Tests`, `Benchmarks`
- **VST**: `LiveSPICEVst` (managed VST3 via AudioPlugSharp 0.6.10)
- **Apps**: `LiveSPICE` (main WPF/WinForms hybrid `WinExe`), `MockVst` (test host for the VST plugin)

## .NET targets — read this before building

The repo is on branch `upgrade-to-NET10` mid-migration. **The csproj files target `net10.0` / `net10.0-windows`**, but the **CI workflows in `.github/workflows/` still pin `dotnet-version: 6.0.x` and `8.0.x` and publish against `net6.0-windows` / `net8.0-windows`.** Updating CI is a deliberate later step in the upgrade plan; don't "fix" the version drift unless that is the task.

Upgrade tracking lives in `.github/upgrades/scenarios/dotnet-version-upgrade/` (plan, per-tier task files, execution-log).

## Build & test commands (PowerShell, Windows)

Publish the main app — from `LiveSPICE\`:

```
dotnet publish -c Release --framework net10.0-windows /p:DebugType=None /p:UseSharedCompilation=false /p:UseRazorBuildServer=false
```

Publish the VST plugin — from `LiveSPICEVst\`, same flags. The snapshot workflow then copies `Circuit\Components\*.xml` into the publish output (see below).

Run the schematic-based test suite — from `Tests\`:

```
dotnet run -c Release --framework net10.0-windows test "Circuits\*.schx"
dotnet run -c Release --framework net10.0-windows test "Examples\*.schx"
```

If invoking the older CI-targeted TFMs (e.g. for parity with `test.yml`), substitute `net6.0-windows`.

## Testing model — there is no xUnit/NUnit

`Tests` is a `System.CommandLine` executable, not a unit-test project. Test cases are **schematic files** (`.schx`) under `Tests/Circuits/` and `Tests/Examples/`, run through the `test` verb of the executable. Adding a new test means adding a new schematic, not a new `[Fact]`. Don't reach for `dotnet test`.

## Circuit components are XML, not code

Component definitions (Diodes, OpAmps, Transistors, Tubes, etc.) live in `Circuit/Components/*.xml` and are loaded at runtime. The snapshot/release workflows copy this directory into both the LiveSPICE and LiveSPICEVst publish outputs — keep that in mind if you add a new component file, since a publish from `dotnet publish` alone won't include it.

## CI workflows

- `.github/workflows/test.yml` — runs on push/PR. Publishes both apps and runs the `.schx` test/example suites on `windows-latest`.
- `.github/workflows/snapshot.yml` — runs on pushes to `master` and on manual dispatch. Produces portable zip artifacts (`LiveSPICE-*.zip`, `LiveSPICEVst-*.zip`).
- `.github/workflows/release.yml` — runs on GitHub releases. Builds the Inno Setup installer driven by `LiveSPICESetup.iss` at the repo root.

## Key package versions

AudioPlugSharp 0.6.10, Dirkster.AvalonDock 4.40.0, DotNetProjects.Extended.Wpf.Toolkit 4.6.86, MathNet.Numerics 4.12.0, BenchmarkDotNet 0.13.1, System.CommandLine 2.0.0-beta1.
