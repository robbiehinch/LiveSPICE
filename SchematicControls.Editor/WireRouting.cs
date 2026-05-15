using System;
using System.Collections.Generic;
using System.Linq;
using SchematicControls.Editor.Edits;

namespace SchematicControls.Editor
{
    /// <summary>
    /// Pure geometric helpers for routing wires on the schematic grid. Lifted out of
    /// WPF <c>SchematicEditor</c> so the logic is shared by both heads. The helpers
    /// take an <see cref="ISchematicHost"/> for context (existing wires, terminals)
    /// rather than the head-specific control.
    /// </summary>
    public static class WireRouting
    {
        /// <summary>
        /// Given a mouse-trail of grid-snapped coordinates, choose the better of two
        /// L-shaped paths (horizontal-first vs vertical-first) between the first and
        /// last point. Picks the path that minimises total perpendicular distance from
        /// the intermediate trail samples.
        /// </summary>
        public static List<Circuit.Coord> FindWirePath(List<Circuit.Coord> mouseTrail)
        {
            Circuit.Coord a = mouseTrail[0];
            Circuit.Coord b = mouseTrail[mouseTrail.Count - 1];

            List<Circuit.Coord> hFirst = new List<Circuit.Coord> { a, new Circuit.Coord(b.x, a.y), b };
            List<Circuit.Coord> vFirst = new List<Circuit.Coord> { a, new Circuit.Coord(a.x, b.y), b };

            double Cost(List<Circuit.Coord> p)
            {
                double total = 0;
                foreach (Circuit.Coord sample in mouseTrail)
                {
                    double best = double.PositiveInfinity;
                    for (int k = 0; k < p.Count - 1; k++)
                        best = Math.Min(best, SegmentDistance(p[k], p[k + 1], sample));
                    total += best;
                }
                return total;
            }

            return Cost(hFirst) <= Cost(vFirst) ? hFirst : vFirst;
        }

        /// <summary>
        /// Add a single wire between two grid-aligned points. Merges existing coincident
        /// wires that lie on the same axis, and splits the new run at any terminals it
        /// crosses. Wraps everything in a single edit group.
        /// </summary>
        public static void AddWire(ISchematicHost host, Circuit.Coord A, Circuit.Coord B)
        {
            if (A == B) return;
            // WPF version asserts orthogonality — keep the soft check (no Debug.Assert here
            // since this can run on user input).

            host.BeginEditGroup();
            try
            {
                List<Circuit.Wire> overlapping = CoincidentWires(host.Wires, A, B).ToList();

                Circuit.Coord a = new Circuit.Coord(
                    Min(overlapping.Select(w => Math.Min(w.A.x, w.B.x)), Math.Min(A.x, B.x)),
                    Min(overlapping.Select(w => Math.Min(w.A.y, w.B.y)), Math.Min(A.y, B.y)));
                Circuit.Coord b = new Circuit.Coord(
                    Max(overlapping.Select(w => Math.Max(w.A.x, w.B.x)), Math.Max(A.x, B.x)),
                    Max(overlapping.Select(w => Math.Max(w.A.y, w.B.y)), Math.Max(A.y, B.y)));

                List<Circuit.Coord> terminals = new List<Circuit.Coord> { a, b };
                foreach (Circuit.Element el in host.InRect(a - 1, b + 1))
                {
                    foreach (Circuit.Terminal t in el.Terminals)
                    {
                        if (Circuit.Wire.PointOnSegment(el.MapTerminal(t), a, b))
                        {
                            if (!(el is Circuit.Wire) ||
                                el.Terminals.Any(k => !Circuit.Wire.PointOnLine(el.MapTerminal(k), a, b)))
                                terminals.Add(el.MapTerminal(t));
                        }
                    }

                    if (el is Circuit.Wire w)
                    {
                        Circuit.Coord ia = w.MapTerminal(w.Anode);
                        Circuit.Coord ib = w.MapTerminal(w.Cathode);
                        if (Circuit.Wire.PointOnLine(A, ia, ib) && !Circuit.Wire.PointOnLine(B, ia, ib))
                            terminals.Add(A);
                        else if (Circuit.Wire.PointOnLine(B, ia, ib) && !Circuit.Wire.PointOnLine(A, ia, ib))
                            terminals.Add(B);
                    }
                }

                terminals.Sort((t1, t2) => t1.x == t2.x ? t1.y.CompareTo(t2.y) : t1.x.CompareTo(t2.x));

                if (overlapping.Count > 0)
                    host.Edits.Do(new RemoveElements(host.Schematic, overlapping));

                List<Circuit.Element> toAdd = new List<Circuit.Element>();
                for (int i = 0; i < terminals.Count - 1; i++)
                    if (terminals[i] != terminals[i + 1])
                        toAdd.Add(new Circuit.Wire(terminals[i], terminals[i + 1]));

                if (toAdd.Count > 0)
                    host.Edits.Do(new AddElements(host.Schematic, toAdd));
            }
            finally
            {
                host.EndEditGroup();
            }
        }

        public static void AddWire(ISchematicHost host, IList<Circuit.Coord> path)
        {
            if (path == null || path.Count < 2) return;
            host.BeginEditGroup();
            try
            {
                for (int i = 0; i < path.Count - 1; i++)
                    AddWire(host, path[i], path[i + 1]);
            }
            finally
            {
                host.EndEditGroup();
            }
        }

        private static IEnumerable<Circuit.Wire> CoincidentWires(IEnumerable<Circuit.Wire> wires, Circuit.Coord A, Circuit.Coord B)
        {
            return wires.Where(i =>
                Circuit.Wire.PointOnLine(A, i.A, i.B) && Circuit.Wire.PointOnLine(B, i.A, i.B) &&
                (Circuit.Wire.PointOnSegment(A, i.A, i.B) ||
                 Circuit.Wire.PointOnSegment(B, i.A, i.B) ||
                 Circuit.Wire.PointOnSegment(i.A, A, B) ||
                 Circuit.Wire.PointOnSegment(i.B, A, B)));
        }

        private static double SegmentDistance(Circuit.Coord a, Circuit.Coord b, Circuit.Coord p)
        {
            // Same approximation the WPF version used (comment in original: "TODO: This is wrong").
            // Replicated verbatim so wire-routing behaviour matches.
            double d1 = Distance(p, a);
            double d2 = Distance(p, b);
            double perp = a.y == b.y ? Math.Abs(p.y - a.y) : Math.Abs(p.x - a.x);
            return Math.Min(Math.Min(d1, d2), perp);
        }

        private static double Distance(Circuit.Coord a, Circuit.Coord b)
        {
            Circuit.Coord d = b - a;
            return Math.Sqrt(d.x * (double)d.x + d.y * (double)d.y);
        }

        private static int Min(IEnumerable<int> values, int fallback)
        {
            int result = fallback;
            foreach (int v in values) if (v < result) result = v;
            return result;
        }

        private static int Max(IEnumerable<int> values, int fallback)
        {
            int result = fallback;
            foreach (int v in values) if (v > result) result = v;
            return result;
        }
    }
}
