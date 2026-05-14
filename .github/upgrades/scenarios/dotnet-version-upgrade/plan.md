# .NET 10.0 Upgrade Plan

### Selected Strategy
**Bottom-Up (Dependency-First)** — Upgrade from leaf nodes to root applications, tier by tier.
**Rationale**: 12 projects with a 5-tier dependency graph and significant breaking changes (4453 mandatory issues concentrated in upper tiers). Bottom-up ensures each layer is stable before dependents are upgraded.

```
Tier 4: [LiveSPICE] [MockVst]
             ↓          ↓
Tier 3: [LiveSPICEVst]
         ↓         ↓
Tier 2: [SchematicControls] [Tests] [Benchmarks] [Asio] [WaveAudio]
              ↓         ↓                          ↓        ↓
Tier 1: [Circuit] [Audio]
           ↓   ↓     ↓
Tier 0: [ComputerAlgebra] [Util]
```

## Overview

**Target**: Upgrade all 12 projects from .NET Standard 2.0 / .NET 6.0 / .NET 8.0 to .NET 10.0
**Scope**: 12 projects across 5 dependency tiers. Libraries currently on netstandard2.0 will move to net10.0. Applications on net6.0-windows and net8.0-windows will move to net10.0-windows.

## Tasks

### 01-foundation-libs: Upgrade foundation libraries (Tier 0)

Upgrade ComputerAlgebra and Util from netstandard2.0 to net10.0. ComputerAlgebra has 1 source-incompatible API issue and 1 behavioral change. Util is straightforward with only a framework-included NuGet package to clean up. Both are leaf nodes with no internal dependencies.

**Done when**: Both projects target net10.0, build successfully, and all NuGet package references are cleaned up.

---

### 02-mid-level-libs: Upgrade mid-level libraries (Tier 1)

Upgrade Circuit and Audio from netstandard2.0 to net10.0. Circuit depends on Util and ComputerAlgebra. Audio depends on Util and has a deprecated NuGet package to address. Both have framework-included packages to remove.

**Done when**: Both projects target net10.0, build successfully, deprecated package resolved, and framework-included packages cleaned up.

---

### 03-upper-libs-and-tests: Upgrade upper libraries, tests, and benchmarks (Tier 2)

Upgrade SchematicControls, Tests, Benchmarks, Asio, and WaveAudio. SchematicControls (WPF, 353 mandatory issues) is the highest-risk project in this tier. Tests and Benchmarks move from net6.0-windows to net10.0-windows. Asio and WaveAudio are straightforward library upgrades.

**Done when**: All 5 projects target net10.0(-windows), build successfully, and tests pass.

---

### 04-livespicevst: Upgrade LiveSPICEVst (Tier 3)

Upgrade LiveSPICEVst from net8.0-windows to net10.0-windows. Has an incompatible NuGet package (NuGet.0001), binary-incompatible APIs, and behavioral changes. The incompatible package is the primary risk — may need an alternative or workaround.

**Done when**: Project targets net10.0-windows, incompatible package resolved, builds successfully.

---

### 05-apps: Upgrade top-level applications (Tier 4)

Upgrade LiveSPICE and MockVst to net10.0-windows. LiveSPICE is the main application with 3734 mandatory issues (mostly WPF-related). MockVst depends on LiveSPICEVst and has 31 mandatory issues.

**Done when**: Both projects target net10.0-windows, full solution builds successfully, and all tests pass.
