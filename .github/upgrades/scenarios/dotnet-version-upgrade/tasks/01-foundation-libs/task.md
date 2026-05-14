# 01-foundation-libs: Upgrade foundation libraries (Tier 0)

Upgrade ComputerAlgebra and Util from netstandard2.0 to net10.0. ComputerAlgebra has 1 source-incompatible API issue and 1 behavioral change. Util is straightforward with only a framework-included NuGet package to clean up. Both are leaf nodes with no internal dependencies.

**Done when**: Both projects target net10.0, build successfully, and all NuGet package references are cleaned up.
