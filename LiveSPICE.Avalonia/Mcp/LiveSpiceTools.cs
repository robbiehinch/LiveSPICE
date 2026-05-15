using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LiveSPICE.Avalonia.Controls;
using LiveSPICE.Avalonia.Services;
using SchematicControls.Editor.Edits;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>
    /// Registers the LiveSPICE-specific MCP tools against an <see cref="McpToolRegistry"/>.
    /// All handlers that mutate the schematic marshal to the UI thread via
    /// <see cref="McpHost.OnUi"/>; probe reads are thread-safe and go straight through
    /// the simulation service's lock.
    /// </summary>
    public static class LiveSpiceTools
    {
        public static void Register(McpToolRegistry registry, McpHost host)
        {
            registry.Register(new McpToolDefinition(
                "get_schematic",
                "Return the current schematic as JSON: list of elements (symbols + wires) with their IDs, coordinates, and component properties.",
                JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
                args => GetSchematic(host)));

            registry.Register(new McpToolDefinition(
                "list_component_types",
                "List all available Circuit.Component subclasses that can be placed via place_component, with their full type names and short names.",
                JsonNode.Parse("""{"type":"object","properties":{"filter":{"type":"string","description":"Optional substring to match (case-insensitive) against the short name."}},"additionalProperties":false}"""),
                args => ListComponentTypes(args)));

            registry.Register(new McpToolDefinition(
                "place_component",
                "Place a Circuit.Component on the schematic. Coordinates are in schematic units (grid is 5 units). Returns the new element id.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "type_name": {"type": "string", "description": "Full or short type name, e.g. 'Circuit.Resistor' or 'Resistor'."},
                        "x": {"type": "integer", "description": "X coordinate (snapped to nearest grid)."},
                        "y": {"type": "integer", "description": "Y coordinate (snapped to nearest grid)."},
                        "rotation": {"type": "integer", "description": "0/1/2/3 quarter turns counter-clockwise.", "default": 0},
                        "flip": {"type": "boolean", "description": "Flip the symbol vertically.", "default": false},
                        "properties": {"type": "object", "description": "Optional component property names → string values to set (parsed by the type's TypeConverter)."}
                      },
                      "required": ["type_name", "x", "y"]
                    }
                """),
                args => PlaceComponent(host, args)));

            registry.Register(new McpToolDefinition(
                "place_wire",
                "Add a wire between two grid coordinates. Wires merge with coincident wires and split at terminals automatically.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "x1": {"type": "integer"}, "y1": {"type": "integer"},
                        "x2": {"type": "integer"}, "y2": {"type": "integer"}
                      },
                      "required": ["x1","y1","x2","y2"]
                    }
                """),
                args => PlaceWire(host, args)));

            registry.Register(new McpToolDefinition(
                "remove_element",
                "Remove an element by id (as returned by get_schematic or place_component). Undoable from the UI.",
                JsonNode.Parse("""{"type":"object","properties":{"id":{"type":"integer"}},"required":["id"]}"""),
                args => RemoveElement(host, args)));

            registry.Register(new McpToolDefinition(
                "set_property",
                "Set a property on the component carried by a symbol element. Value is parsed via the property's TypeConverter, so '10k' works for resistors etc. Returns the prior value.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "id": {"type": "integer", "description": "Symbol element id."},
                        "property": {"type": "string", "description": "Name of the property on the component (e.g. 'Resistance')."},
                        "value": {"type": "string", "description": "New value, parsed via TypeConverter."}
                      },
                      "required": ["id", "property", "value"]
                    }
                """),
                args => SetProperty(host, args)));

            registry.Register(new McpToolDefinition(
                "place_probe",
                "Drop a Circuit.Probe at the given coordinates. Best called while the simulation is running so the scope updates immediately, but works at any time — the probe will be picked up the next time the simulation starts.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "x": {"type": "integer"},
                        "y": {"type": "integer"},
                        "color": {"type": "string", "description": "EdgeType name (Magenta, Cyan, Yellow, Green, Red, Blue, Orange). Cycles through palette if omitted."}
                      },
                      "required": ["x", "y"]
                    }
                """),
                args => PlaceProbe(host, args)));

            registry.Register(new McpToolDefinition(
                "list_probes",
                "List the probes currently active in the running simulation, with their element ids and node assignments.",
                JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
                args => ListProbes(host)));

            registry.Register(new McpToolDefinition(
                "read_probe_samples",
                "Read the most recent samples from a probe's ring buffer. Returns up to 'count' samples, oldest first. Use this to look at the waveform shape.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "id": {"type": "integer", "description": "Probe element id (from list_probes or place_probe)."},
                        "count": {"type": "integer", "description": "Max samples to return (default 1024, cap 4096)."}
                      },
                      "required": ["id"]
                    }
                """),
                args => ReadProbeSamples(host, args)));

            registry.Register(new McpToolDefinition(
                "read_probe_stats",
                "Compute summary statistics over the probe's most recent samples: min, max, peak (abs), mean, RMS, peak-to-peak. Use this to read a node's behaviour without pulling all samples.",
                JsonNode.Parse("""
                    {
                      "type": "object",
                      "properties": {
                        "id": {"type": "integer"},
                        "window": {"type": "integer", "description": "How many recent samples to consider (default all available)."}
                      },
                      "required": ["id"]
                    }
                """),
                args => ReadProbeStats(host, args)));

            registry.Register(new McpToolDefinition(
                "simulation_status",
                "Report whether a simulation is running, its sample rate, oversample/iterations factors, probe count, and most recent input/output peak levels.",
                JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
                args => SimulationStatus(host)));

            registry.Register(new McpToolDefinition(
                "start_simulation",
                "Open the live-simulation window (if not already open) and start the simulation against the current schematic. Uses the most recently saved audio device selection.",
                JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
                args => StartSimulation(host)));

            registry.Register(new McpToolDefinition(
                "stop_simulation",
                "Stop the running simulation (if any). Probes dropped during the simulation are discarded — they only existed on the simulation clone.",
                JsonNode.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""),
                args => StopSimulation(host)));
        }

        // -------------- handlers --------------

        private static async Task<JsonNode> GetSchematic(McpHost host)
        {
            return await host.OnUi(() =>
            {
                Circuit.Schematic s = host.Canvas.Schematic;
                if (s == null) return McpResult.StructuredContent(new JsonObject { ["schematic"] = null });
                JsonObject payload = new JsonObject
                {
                    ["element_count"] = s.Elements.Count(),
                    ["lower_bound"] = SchematicSnapshot.Coord(s.LowerBound),
                    ["upper_bound"] = SchematicSnapshot.Coord(s.UpperBound),
                    ["elements"] = SchematicSnapshot.Elements(s.Elements, host),
                };
                return McpResult.StructuredContent(payload);
            });
        }

        private static Task<JsonNode> ListComponentTypes(JsonNode args)
        {
            string filter = args?["filter"]?.GetValue<string>()?.ToLowerInvariant();
            Type baseType = typeof(Circuit.Component);
            JsonArray arr = new JsonArray();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in types)
                {
                    if (!t.IsPublic || t.IsAbstract) continue;
                    if (!baseType.IsAssignableFrom(t)) continue;
                    if (t.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                    if (filter != null && !t.Name.ToLowerInvariant().Contains(filter)) continue;
                    arr.Add(new JsonObject
                    {
                        ["short_name"] = t.Name,
                        ["full_name"] = t.FullName,
                    });
                }
            }
            return Task.FromResult(McpResult.StructuredContent(new JsonObject { ["types"] = arr }));
        }

        private static async Task<JsonNode> PlaceComponent(McpHost host, JsonNode args)
        {
            string typeName = args?["type_name"]?.GetValue<string>();
            int x = args?["x"]?.GetValue<int>() ?? 0;
            int y = args?["y"]?.GetValue<int>() ?? 0;
            int rotation = args?["rotation"]?.GetValue<int>() ?? 0;
            bool flip = args?["flip"]?.GetValue<bool>() ?? false;
            JsonObject props = args?["properties"] as JsonObject;

            if (string.IsNullOrEmpty(typeName))
                return McpResult.TextContent("type_name is required.", isError: true);

            Type t = ResolveComponentType(typeName);
            if (t == null)
                return McpResult.TextContent("Unknown component type: " + typeName, isError: true);

            return await host.OnUi(() =>
            {
                Circuit.Schematic s = host.Canvas.Schematic;
                if (s == null) return McpResult.TextContent("No schematic open.", isError: true);

                Circuit.Component component;
                try { component = (Circuit.Component)Activator.CreateInstance(t); }
                catch (Exception ex) { return McpResult.TextContent("Cannot instantiate '" + t.Name + "': " + ex.Message, isError: true); }

                if (props != null)
                {
                    List<string> failed = ApplyProperties(component, props);
                    if (failed.Count > 0)
                        return McpResult.TextContent("Property set failed for: " + string.Join(", ", failed), isError: true);
                }

                Circuit.Coord snap = SnapToGrid(new Circuit.Coord(x, y));
                Circuit.Symbol sym = new Circuit.Symbol(component)
                {
                    Position = snap,
                    Rotation = rotation,
                    Flip = flip,
                };
                host.Canvas.Edits.Do(new AddElements(s, new Circuit.Element[] { sym }));
                int id = host.IdFor(sym);
                return McpResult.StructuredContent(new JsonObject
                {
                    ["id"] = id,
                    ["kind"] = "Symbol",
                    ["short_name"] = t.Name,
                    ["position"] = SchematicSnapshot.Coord(sym.Position),
                });
            });
        }

        private static async Task<JsonNode> PlaceWire(McpHost host, JsonNode args)
        {
            int x1 = args?["x1"]?.GetValue<int>() ?? 0;
            int y1 = args?["y1"]?.GetValue<int>() ?? 0;
            int x2 = args?["x2"]?.GetValue<int>() ?? 0;
            int y2 = args?["y2"]?.GetValue<int>() ?? 0;

            return await host.OnUi(() =>
            {
                Circuit.Schematic s = host.Canvas.Schematic;
                if (s == null) return McpResult.TextContent("No schematic open.", isError: true);
                Circuit.Coord a = SnapToGrid(new Circuit.Coord(x1, y1));
                Circuit.Coord b = SnapToGrid(new Circuit.Coord(x2, y2));
                SchematicControls.Editor.WireRouting.AddWire(host.Canvas, a, b);
                return McpResult.StructuredContent(new JsonObject
                {
                    ["a"] = SchematicSnapshot.Coord(a),
                    ["b"] = SchematicSnapshot.Coord(b),
                });
            });
        }

        private static async Task<JsonNode> RemoveElement(McpHost host, JsonNode args)
        {
            int id = args?["id"]?.GetValue<int>() ?? 0;
            if (!host.TryResolve(id, out Circuit.Element e))
                return McpResult.TextContent("No element with id " + id, isError: true);
            return await host.OnUi(() =>
            {
                Circuit.Schematic s = host.Canvas.Schematic;
                if (s == null) return McpResult.TextContent("No schematic open.", isError: true);
                host.Canvas.Edits.Do(new RemoveElements(s, new Circuit.Element[] { e }));
                host.ForgetId(e);
                return McpResult.StructuredContent(new JsonObject { ["removed_id"] = id });
            });
        }

        private static async Task<JsonNode> SetProperty(McpHost host, JsonNode args)
        {
            int id = args?["id"]?.GetValue<int>() ?? 0;
            string propName = args?["property"]?.GetValue<string>();
            string value = args?["value"]?.GetValue<string>();
            if (string.IsNullOrEmpty(propName))
                return McpResult.TextContent("'property' is required.", isError: true);
            if (!host.TryResolve(id, out Circuit.Element e))
                return McpResult.TextContent("No element with id " + id, isError: true);
            if (!(e is Circuit.Symbol sym))
                return McpResult.TextContent("Element " + id + " is not a Symbol.", isError: true);

            Circuit.Component c = sym.Component;
            PropertyInfo p = c.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite)
                return McpResult.TextContent("Property '" + propName + "' not found on " + c.GetType().Name, isError: true);

            object prior;
            try { prior = p.GetValue(c, null); } catch { prior = null; }
            TypeConverter conv = TypeDescriptor.GetConverter(p.PropertyType);
            object parsed;
            try { parsed = conv.CanConvertFrom(typeof(string)) ? conv.ConvertFromInvariantString(value) : value; }
            catch (Exception ex) { return McpResult.TextContent("Cannot parse '" + value + "' as " + p.PropertyType.Name + ": " + ex.Message, isError: true); }

            return await host.OnUi(() =>
            {
                host.Canvas.Edits.Do(new PropertyEdit(c, p, parsed, prior));
                return McpResult.StructuredContent(new JsonObject
                {
                    ["id"] = id,
                    ["property"] = propName,
                    ["new_value"] = parsed?.ToString(),
                    ["prior_value"] = prior?.ToString(),
                });
            });
        }

        private static async Task<JsonNode> PlaceProbe(McpHost host, JsonNode args)
        {
            int x = args?["x"]?.GetValue<int>() ?? 0;
            int y = args?["y"]?.GetValue<int>() ?? 0;
            string colorName = args?["color"]?.GetValue<string>();
            Circuit.EdgeType color = ParseColor(colorName, host);

            return await host.OnUi(() =>
            {
                // Prefer placing on the simulation clone if a simulation is running, so the
                // probe takes effect immediately. Otherwise drop it on the source schematic.
                LiveSimulationService svc = host.SimulationService;
                SchematicCanvas target = svc != null ? host.SimulationWindow.SimCanvas : host.Canvas;
                Circuit.Schematic s = target.Schematic;
                if (s == null) return McpResult.TextContent("No schematic open.", isError: true);

                Circuit.Probe probe = new Circuit.Probe(color);
                Circuit.Coord snap = SnapToGrid(new Circuit.Coord(x, y));
                Circuit.Symbol sym = new Circuit.Symbol(probe) { Position = snap };
                target.Edits.Do(new AddElements(s, new Circuit.Element[] { sym }));
                int id = host.IdFor(sym);
                return McpResult.StructuredContent(new JsonObject
                {
                    ["id"] = id,
                    ["color"] = color.ToString(),
                    ["position"] = SchematicSnapshot.Coord(snap),
                    ["in_simulation"] = svc != null,
                });
            });
        }

        private static Task<JsonNode> ListProbes(McpHost host)
        {
            LiveSimulationService svc = host.SimulationService;
            if (svc == null) return Task.FromResult(McpResult.StructuredContent(new JsonObject { ["probes"] = new JsonArray(), ["running"] = false }));

            JsonArray arr = new JsonArray();
            foreach (Circuit.Probe p in svc.Probes)
            {
                // Find the symbol carrying this probe so we can return its element id +
                // position. Search by reference identity through the simulation clone.
                Circuit.Symbol owner = svc.Schematic?.Elements
                    .OfType<Circuit.Symbol>()
                    .FirstOrDefault(s => ReferenceEquals(s.Component, p));
                JsonObject o = new JsonObject
                {
                    ["color"] = p.Color.ToString(),
                    ["node"] = p.V?.ToString(),
                };
                if (owner != null)
                {
                    o["id"] = host.IdFor(owner);
                    o["position"] = SchematicSnapshot.Coord(owner.Position);
                }
                arr.Add(o);
            }
            return Task.FromResult(McpResult.StructuredContent(new JsonObject { ["probes"] = arr, ["running"] = true }));
        }

        private static Task<JsonNode> ReadProbeSamples(McpHost host, JsonNode args)
        {
            int id = args?["id"]?.GetValue<int>() ?? 0;
            int count = Math.Clamp(args?["count"]?.GetValue<int>() ?? 1024, 1, 4096);
            LiveSimulationService svc = host.SimulationService;
            if (svc == null) return Task.FromResult(McpResult.TextContent("No simulation running.", isError: true));
            Circuit.Probe probe = ResolveProbe(host, id);
            if (probe == null) return Task.FromResult(McpResult.TextContent("Element " + id + " is not a probe (or not in the running simulation).", isError: true));

            double[] all = svc.SnapshotProbe(probe);
            int n = Math.Min(count, all.Length);
            JsonArray samples = new JsonArray();
            for (int i = all.Length - n; i < all.Length; i++) samples.Add(all[i]);
            return Task.FromResult(McpResult.StructuredContent(new JsonObject
            {
                ["id"] = id,
                ["sample_count"] = n,
                ["sample_rate"] = svc.SampleRate,
                ["samples"] = samples,
            }));
        }

        private static Task<JsonNode> ReadProbeStats(McpHost host, JsonNode args)
        {
            int id = args?["id"]?.GetValue<int>() ?? 0;
            int window = args?["window"]?.GetValue<int>() ?? int.MaxValue;
            LiveSimulationService svc = host.SimulationService;
            if (svc == null) return Task.FromResult(McpResult.TextContent("No simulation running.", isError: true));
            Circuit.Probe probe = ResolveProbe(host, id);
            if (probe == null) return Task.FromResult(McpResult.TextContent("Element " + id + " is not a probe.", isError: true));

            double[] all = svc.SnapshotProbe(probe);
            int n = Math.Min(window, all.Length);
            if (n == 0) return Task.FromResult(McpResult.TextContent("Probe buffer is empty.", isError: true));

            double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0, sumSq = 0;
            for (int i = all.Length - n; i < all.Length; i++)
            {
                double v = all[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
                sumSq += v * v;
            }
            double mean = sum / n;
            double rms = Math.Sqrt(sumSq / n);

            return Task.FromResult(McpResult.StructuredContent(new JsonObject
            {
                ["id"] = id,
                ["window_samples"] = n,
                ["sample_rate"] = svc.SampleRate,
                ["min"] = min,
                ["max"] = max,
                ["peak_to_peak"] = max - min,
                ["abs_peak"] = Math.Max(Math.Abs(min), Math.Abs(max)),
                ["mean"] = mean,
                ["rms"] = rms,
            }));
        }

        private static Task<JsonNode> SimulationStatus(McpHost host)
        {
            LiveSimulationService svc = host.SimulationService;
            JsonObject o = new JsonObject { ["running"] = svc != null && svc.IsRunning };
            if (svc != null)
            {
                o["sample_rate"] = svc.SampleRate;
                o["oversample"] = svc.Oversample;
                o["iterations"] = svc.Iterations;
                o["probe_count"] = svc.Probes.Count;
                o["input_peaks"] = ToArray(svc.InputPeaks);
                o["output_peaks"] = ToArray(svc.OutputPeaks);
            }
            return Task.FromResult(McpResult.StructuredContent(o));
        }

        private static async Task<JsonNode> StartSimulation(McpHost host)
        {
            return await host.OnUi(() =>
            {
                if (host.SimulationWindow != null && host.SimulationService != null && host.SimulationService.IsRunning)
                    return McpResult.TextContent("Simulation already running.");
                host.MainWindow.OpenSimulationFromMcp(startImmediately: true);
                LiveSimulationService svc = host.SimulationService;
                return McpResult.StructuredContent(new JsonObject
                {
                    ["running"] = svc != null && svc.IsRunning,
                    ["note"] = svc == null ? "Window opened — start failed; check audio device selection." : "Simulation started.",
                });
            });
        }

        private static async Task<JsonNode> StopSimulation(McpHost host)
        {
            return await host.OnUi(() =>
            {
                if (host.SimulationWindow == null) return McpResult.TextContent("No simulation window open.");
                host.SimulationWindow.StopFromMcp();
                return McpResult.StructuredContent(new JsonObject { ["running"] = false });
            });
        }

        // -------------- helpers --------------

        private static Circuit.Coord SnapToGrid(Circuit.Coord c)
        {
            const int Grid = 5;
            int x = (int)Math.Round(c.x / (double)Grid) * Grid;
            int y = (int)Math.Round(c.y / (double)Grid) * Grid;
            return new Circuit.Coord(x, y);
        }

        private static Type ResolveComponentType(string name)
        {
            Type baseType = typeof(Circuit.Component);
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in types)
                {
                    if (!t.IsPublic || t.IsAbstract) continue;
                    if (!baseType.IsAssignableFrom(t)) continue;
                    if (string.Equals(t.FullName, name, StringComparison.Ordinal)) return t;
                    if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            return null;
        }

        private static List<string> ApplyProperties(Circuit.Component c, JsonObject props)
        {
            List<string> failed = new List<string>();
            foreach (KeyValuePair<string, JsonNode> kv in props)
            {
                PropertyInfo p = c.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) { failed.Add(kv.Key); continue; }
                TypeConverter conv = TypeDescriptor.GetConverter(p.PropertyType);
                try
                {
                    string s = kv.Value?.GetValue<string>();
                    object parsed = conv.CanConvertFrom(typeof(string)) ? conv.ConvertFromInvariantString(s) : s;
                    p.SetValue(c, parsed, null);
                }
                catch { failed.Add(kv.Key); }
            }
            return failed;
        }

        private static Circuit.Probe ResolveProbe(McpHost host, int id)
        {
            if (!host.TryResolve(id, out Circuit.Element e)) return null;
            if (e is Circuit.Symbol s && s.Component is Circuit.Probe p) return p;
            return null;
        }

        private static Circuit.EdgeType ParseColor(string name, McpHost host)
        {
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, ignoreCase: true, out Circuit.EdgeType parsed))
                return parsed;
            // Default palette cycle keyed off the number of probes already placed.
            Circuit.EdgeType[] palette = { Circuit.EdgeType.Magenta, Circuit.EdgeType.Green, Circuit.EdgeType.Yellow,
                                            Circuit.EdgeType.Cyan, Circuit.EdgeType.Orange, Circuit.EdgeType.Red, Circuit.EdgeType.Blue };
            int n = host.SimulationService?.Probes.Count ?? 0;
            return palette[n % palette.Length];
        }

        private static JsonArray ToArray(double[] xs)
        {
            JsonArray a = new JsonArray();
            if (xs == null) return a;
            foreach (double x in xs) a.Add(x);
            return a;
        }
    }
}
