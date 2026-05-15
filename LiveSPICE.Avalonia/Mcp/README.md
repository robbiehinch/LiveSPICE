# LiveSPICE MCP server

The Avalonia head hosts an MCP (Model Context Protocol) server on
`http://localhost:27310/mcp` so Claude Code (and any other MCP client) can
drive the schematic editor and read probe data without you leaving the
terminal.

It uses the Streamable-HTTP transport — one POST endpoint accepting
JSON-RPC 2.0 requests. Localhost-only; no authentication.

## Configuration

Add to your project's `.mcp.json` (or run `claude mcp add`):

```json
{
  "mcpServers": {
    "livespice": {
      "type": "http",
      "url": "http://localhost:27310/mcp"
    }
  }
}
```

Then launch the Avalonia head (`dotnet run --project LiveSPICE.Avalonia`)
before starting Claude Code. The tools appear under the `livespice` server
when you type `/mcp` in a Claude Code session.

To disable the server or change the port, edit
`%AppData%\LiveSPICE\settings.json`:

```json
{
  "McpEnabled": false,
  "McpPort": 27310
}
```

## Tools

### Schematic inspection / editing

| Tool | Description |
| --- | --- |
| `get_schematic` | Dump current schematic as JSON: elements (with IDs), positions, component properties. |
| `list_component_types` | Enumerate placeable `Circuit.Component` types. Optional `filter` substring. |
| `place_component` | Drop a component at `(x, y)`. Args: `type_name` (short or full), `x`, `y`, optional `rotation` (0-3), `flip`, `properties` (string-keyed object parsed via TypeConverter — `"Resistance": "4.7k"` works). Returns the new element ID. |
| `place_wire` | Add a wire from `(x1, y1)` to `(x2, y2)`. Automatically merges coincident wires and splits at terminals. |
| `remove_element` | Remove an element by ID (from `get_schematic` or a place tool). Undoable from the UI. |
| `set_property` | Set a property on the component carried by a symbol. Value is parsed by TypeConverter; returns the prior value. |

### Simulation control + probe data

| Tool | Description |
| --- | --- |
| `simulation_status` | Whether a simulation is running; sample rate, oversample, iterations, probe count, in/out peak meters. |
| `start_simulation` | Open the live-simulation window if needed and start. Uses the audio device settings persisted from the last UI selection. |
| `stop_simulation` | Stop and discard the simulation clone (probes added during the sim are discarded). |
| `place_probe` | Drop a `Circuit.Probe` at `(x, y)`. Goes on the simulation clone if one's running (so the scope picks it up immediately), otherwise on the source schematic. Optional `color` (`Magenta`, `Cyan`, etc.); cycles through a palette if omitted. |
| `list_probes` | Active probes in the running simulation: element IDs, positions, node names. |
| `read_probe_samples` | Last N samples (default 1024, cap 4096) from a probe's ring buffer, oldest first. Includes the sample rate. |
| `read_probe_stats` | Summary over a window of recent samples: `min`, `max`, `peak_to_peak`, `abs_peak`, `mean`, `rms`. Use this instead of `read_probe_samples` when you only need to know how a node is behaving. |

## Threading model

- Tool handlers run on the HTTP listener's worker threads.
- Anything that touches the schematic, the canvas, or the edit stack is
  marshalled to `Dispatcher.UIThread` via `McpHost.OnUi`. All edits go
  through the same `EditStack` the UI uses, so they appear in the undo
  history.
- Probe reads (`read_probe_samples`, `read_probe_stats`) take the
  simulation service's internal lock — safe to call from any thread, no UI
  blocking.

## Example session

```
> Claude, place a 10k resistor at (50, 50), a 100n cap at (80, 50),
  and wire them in series with a probe on the junction.

[Claude calls place_component R=10k, place_component C=100n,
 place_wire (50,50)→(80,50), place_probe (65,50)]

> Now run the simulation and tell me the RMS at the probe.

[Claude calls start_simulation, then read_probe_stats with window=4096]
```

## Limitations

- No streaming of probe samples — Claude polls. For continuous analysis,
  call `read_probe_stats` periodically (or `read_probe_samples` if you
  want the waveform shape).
- No `save_schematic` / `open_schematic` tools yet — use the File menu in
  the app for those.
- IDs are session-scoped: closing the app resets the ID counter. Within
  a session they're stable across get_schematic calls.
