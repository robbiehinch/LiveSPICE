# LiveSPICE.Avalonia (scaffolding)

Skeleton of the Avalonia UI head described in
`nimbalyst-local/plans/is-it-possible-to-modular-bubble.md`. This is **not** a working
app — it's the starting commit for the multi-week port described in Tier 4 of the plan.

## What's here

- `App.axaml` + `Program.cs` — minimal Avalonia entry point with Fluent theme.
- `MainWindow.axaml` — placeholder shell. Real menus/docking/property-grid/scope are TODO.
- `Drawing/AvaloniaDrawingContext.cs` — paired with WPF's `WpfDrawingContext`, this is
  the abstraction that lets the same `SymbolLayoutRenderer` paint schematics from both
  heads. Wire this into a concrete `Avalonia.Controls.Control.Render` override on the
  Avalonia `SchematicControl` when that lands.
- `Drawing/AvaloniaFormattedText.cs` — `IFormattedText` adapter over Avalonia's
  `FormattedText`.

## What's not here

Everything else: schematic canvas control, editor tools port, AvalonDock →
Dock.Avalonia translation, PropertyGrid, Scope, Library, simulation glue, theming,
file-dialog integration, settings persistence.

See `nimbalyst-local/plans/is-it-possible-to-modular-bubble.md` for the per-file
mapping and the ~2–3 week estimate for the full Tier 4 work.
