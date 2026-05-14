# 03-upper-libs-and-tests: Upgrade upper libraries, tests, and benchmarks (Tier 2)

Upgrade SchematicControls, Tests, Benchmarks, Asio, and WaveAudio. SchematicControls (WPF, 353 mandatory issues) is the highest-risk project in this tier. Tests and Benchmarks move from net6.0-windows to net10.0-windows. Asio and WaveAudio are straightforward library upgrades.

**Done when**: All 5 projects target net10.0(-windows), build successfully, and tests pass.
