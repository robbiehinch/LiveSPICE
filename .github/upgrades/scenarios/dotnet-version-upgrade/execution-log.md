
## [2026-05-14 10:57] 01-foundation-libs

Upgraded ComputerAlgebra and Util from netstandard2.0 to net10.0. Removed framework-included packages (Microsoft.CSharp, System.Data.DataSetExtensions) from both projects. Both build successfully.


## [2026-05-14 10:58] 02-mid-level-libs

Upgraded Circuit and Audio from netstandard2.0 to net10.0. Removed framework-included packages (Microsoft.CSharp, System.Data.DataSetExtensions, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe). Both build successfully.


## [2026-05-14 11:00] 03-upper-libs-and-tests

Upgraded all 5 Tier 2 projects. SchematicControls, Tests, Benchmarks → net10.0-windows. Asio, WaveAudio → net10.0. Removed framework-included packages (Microsoft.CSharp, System.Data.DataSetExtensions, Microsoft.Win32.Registry). All build successfully. SchematicControls WPF binary-compat warnings are non-breaking.

