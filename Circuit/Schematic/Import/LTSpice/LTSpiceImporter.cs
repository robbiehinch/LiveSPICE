using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// Orchestrates the conversion of a parsed LTSpice .asc sheet into a LiveSPICE Schematic.
    /// </summary>
    public static class LTSpiceImporter
    {
        // LTSpice grid is 16 units; LiveSPICE grid is 10 units. Scale factor = 10/16.
        private const int LT_GRID = 16;
        private const int LS_GRID = 10;

        public static (Schematic, ImportReport) Import(string path)
        {
            return Import(path, NullPartLookup.Instance);
        }

        public static (Schematic, ImportReport) Import(string path, IPartLookup parts)
        {
            using (StreamReader sr = new StreamReader(path))
                return Import(sr, parts);
        }

        public static (Schematic, ImportReport) Import(TextReader reader, IPartLookup parts)
        {
            ImportReport report = new ImportReport();
            LTSpiceSheet sheet = AsciiParser.Parse(reader, report);

            Schematic schematic = new Schematic();

            // Translate symbols.
            foreach (LTSymbol s in sheet.Symbols)
            {
                if (!Whitelist.TryGet(s.SymbolName, out WhitelistEntry entry))
                {
                    report.Warning(s.LineNumber, "Unsupported LTSpice symbol '" + s.SymbolName + "' â€” skipped.");
                    continue;
                }

                Component component;
                try
                {
                    component = entry.Factory(s, parts, report);
                }
                catch (Exception ex)
                {
                    report.Error(s.LineNumber, "Failed to build component for symbol '" + s.SymbolName + "': " + ex.Message);
                    continue;
                }
                if (component == null) continue;

                Symbol symbol = new Symbol(component);

                // Map orientation to (rotation, flip).
                var (rotation, flip) = MapOrientation(s.Orientation);
                symbol.Rotation = rotation;
                symbol.Flip = flip;

                // Place the symbol such that the first pin's LS-global coordinate equals
                // the scaled LT first-pin coordinate. That anchors connectivity for the
                // primary pin and lets us bridge-wire the others.
                Coord position = ComputeSymbolPosition(s, entry, symbol);
                symbol.Position = position;

                schematic.Add(symbol);

                // Bridge wires from each LS pin global to the LT-scaled pin position.
                // This guarantees electrical connectivity even when pin spacing differs
                // between LTSpice and LiveSPICE.
                AddBridgeWires(schematic, s, entry, symbol, component, report);
            }

            // Translate raw wires.
            foreach (LTWire w in sheet.Wires)
            {
                Coord a = LtToLs(new Coord(w.X1, w.Y1));
                Coord b = LtToLs(new Coord(w.X2, w.Y2));
                if (a == b) continue;
                schematic.Add(new Wire(a, b));

                // Off-grid warning.
                if (w.X1 % LT_GRID != 0 || w.Y1 % LT_GRID != 0 ||
                    w.X2 % LT_GRID != 0 || w.Y2 % LT_GRID != 0)
                    report.Warning(w.LineNumber, "WIRE endpoint not on LTSpice 16-unit grid; rounded.");
            }

            // Translate flags: "0" -> Ground, named flag -> NamedWire. Give each
            // instance a unique Name so Schematic.Build doesn't choke on duplicates.
            int groundCounter = 0;
            int namedWireCounter = 0;
            foreach (LTFlag f in sheet.Flags)
            {
                Coord pos = LtToLs(new Coord(f.X, f.Y));
                if (f.Name == "0")
                {
                    Ground g = new Ground();
                    g.Name = "GND" + (++groundCounter);
                    Symbol gs = new Symbol(g);
                    gs.Position = pos;
                    schematic.Add(gs);
                }
                else if (!string.IsNullOrWhiteSpace(f.Name))
                {
                    NamedWire nw = new NamedWire();
                    nw.WireName = f.Name;
                    nw.Name = "NW" + (++namedWireCounter);
                    Symbol nws = new Symbol(nw);
                    nws.Position = pos;
                    schematic.Add(nws);
                }
            }

            // Translate text directives into Label decorations.
            foreach (LTText t in sheet.Texts)
            {
                if (string.IsNullOrWhiteSpace(t.Content)) continue;
                Label lbl = new Label();
                lbl.Text = t.IsSpiceDirective ? "!" + t.Content : t.Content;
                Symbol ls = new Symbol(lbl);
                ls.Position = LtToLs(new Coord(t.X, t.Y));
                schematic.Add(ls);
                if (t.IsSpiceDirective)
                    report.Info(t.LineNumber, "SPICE directive preserved as a label: " + t.Content);
            }

            return (schematic, report);
        }

        // === Coordinate conversion ===

        /// <summary>
        /// Convert an LTSpice coordinate to a LiveSPICE coordinate.
        /// LiveSPICE has the same screen-coordinate convention (y grows downward when drawn),
        /// so this is a pure scale.
        /// </summary>
        public static Coord LtToLs(Coord lt)
        {
            return new Coord(
                RoundToGrid(lt.x * LS_GRID, LT_GRID * LS_GRID) / LT_GRID,
                RoundToGrid(lt.y * LS_GRID, LT_GRID * LS_GRID) / LT_GRID);
        }

        // Round v to the nearest multiple of step (positive step).
        private static int RoundToGrid(int v, int step)
        {
            int half = step / 2;
            if (v >= 0) return ((v + half) / step) * step;
            return -(((-v + half) / step) * step);
        }

        /// <summary>
        /// Apply an LTSpice orientation transform to a pin offset expressed in R0 coordinates.
        /// LTSpice uses screen coordinates (y grows downward).
        /// R90  rotates 90Â° clockwise:  (x, y) â†’ (-y,  x)
        /// R180 rotates 180Â°:           (x, y) â†’ (-x, -y)
        /// R270 rotates 270Â° clockwise: (x, y) â†’ ( y, -x)
        /// M0   mirrors horizontally:   (x, y) â†’ (-x,  y)
        /// M90  = M0 then R90:          (x, y) â†’ (-y, -x)
        /// M180 = M0 then R180:         (x, y) â†’ ( x, -y)
        /// M270 = M0 then R270:         (x, y) â†’ ( y,  x)
        /// </summary>
        public static Coord ApplyLTOrientation(Coord r0, LTOrientation o)
        {
            int x = r0.x, y = r0.y;
            switch (o)
            {
                case LTOrientation.R0: return new Coord(x, y);
                case LTOrientation.R90: return new Coord(-y, x);
                case LTOrientation.R180: return new Coord(-x, -y);
                case LTOrientation.R270: return new Coord(y, -x);
                case LTOrientation.M0: return new Coord(-x, y);
                case LTOrientation.M90: return new Coord(-y, -x);
                case LTOrientation.M180: return new Coord(x, -y);
                case LTOrientation.M270: return new Coord(y, x);
                default: return new Coord(x, y);
            }
        }

        // Map LTSpice orientation to a (LiveSPICE rotation [quarter-turns], LiveSPICE flip) pair.
        // The mapping is approximate visually but does not affect electrical connectivity, since
        // bridge wires close the gap between LS pin globals and LT-scaled pin positions.
        private static (int rotation, bool flip) MapOrientation(LTOrientation o)
        {
            switch (o)
            {
                case LTOrientation.R0: return (0, false);
                case LTOrientation.R90: return (3, false);
                case LTOrientation.R180: return (2, false);
                case LTOrientation.R270: return (1, false);
                case LTOrientation.M0: return (0, true);
                case LTOrientation.M90: return (3, true);
                case LTOrientation.M180: return (2, true);
                case LTOrientation.M270: return (1, true);
                default: return (0, false);
            }
        }

        // === Symbol placement and bridge wires ===

        private static Coord ComputeSymbolPosition(LTSymbol s, WhitelistEntry entry, Symbol placedSymbol)
        {
            // Place the LS symbol so that its body roughly overlaps the LTSpice body.
            // We anchor on the LT symbol's centroid (mean of pin offsets) and convert to LS coords.
            Coord centroid = new Coord(0, 0);
            int count = 0;
            foreach (PinSpec p in entry.Pins)
            {
                Coord rotated = ApplyLTOrientation(p.LTOffsetR0, s.Orientation);
                centroid = new Coord(centroid.x + rotated.x, centroid.y + rotated.y);
                count++;
            }
            if (count == 0) return LtToLs(new Coord(s.X, s.Y));
            Coord ltCenter = new Coord(s.X + centroid.x / count, s.Y + centroid.y / count);
            return LtToLs(ltCenter);
        }

        private static void AddBridgeWires(
            Schematic schematic,
            LTSymbol ltSymbol,
            WhitelistEntry entry,
            Symbol placedSymbol,
            Component component,
            ImportReport report)
        {
            // For each pin in the whitelist, compute:
            //   1. The LS-global coordinate of the corresponding LS Terminal (where the wire really lands).
            //   2. The LT-scaled coordinate where external LT wires terminate.
            // If they differ, add a short Wire bridging them.
            foreach (PinSpec p in entry.Pins)
            {
                Terminal terminal = p.Selector(component);
                if (terminal == null) continue;

                // LS global pin coord.
                Coord lsPin;
                try
                {
                    lsPin = placedSymbol.MapTerminal(terminal);
                }
                catch
                {
                    continue;
                }

                // LT-scaled pin coord.
                Coord ltPinOffset = ApplyLTOrientation(p.LTOffsetR0, ltSymbol.Orientation);
                Coord ltPin = LtToLs(new Coord(ltSymbol.X + ltPinOffset.x, ltSymbol.Y + ltPinOffset.y));

                if (lsPin == ltPin) continue;
                schematic.Add(new Wire(lsPin, ltPin));
            }
        }
    }
}
