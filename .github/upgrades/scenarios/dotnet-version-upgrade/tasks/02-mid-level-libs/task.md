# 02-mid-level-libs: Upgrade mid-level libraries (Tier 1)

Upgrade Circuit and Audio from netstandard2.0 to net10.0. Circuit depends on Util and ComputerAlgebra. Audio depends on Util and has a deprecated NuGet package to address. Both have framework-included packages to remove.

**Done when**: Both projects target net10.0, build successfully, deprecated package resolved, and framework-included packages cleaned up.
