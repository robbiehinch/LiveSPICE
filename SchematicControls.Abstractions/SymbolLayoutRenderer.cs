using System;
using System.Collections.Generic;
using System.Linq;

namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Head-agnostic translation of a <see cref="Circuit.SymbolLayout"/> into calls on an
    /// <see cref="IDrawingContext"/>. Was previously <c>SymbolControl.DrawLayout</c> with
    /// WPF-specific types; lifted here so both the WPF and Avalonia heads share one renderer.
    /// </summary>
    public static class SymbolLayoutRenderer
    {
        // Same value as ElementControl.TextOutline (alpha 32, black, thickness 0.2)
        private static readonly EdgeStyle TextOutlineEdge =
            new EdgeStyle(DrawColor.FromArgb(32, 0, 0, 0), 0.2);

        /// <summary>
        /// Draw the layout. <paramref name="overrideEdge"/>, if non-null, replaces every per-shape
        /// edge style and also suppresses fills (matching the legacy "if Pen == null" branch).
        /// </summary>
        public static void Draw(
            Circuit.SymbolLayout layout,
            IDrawingContext context,
            IDrawingContextFactory factory,
            DrawMatrix transform,
            EdgeStyle? overrideEdge,
            TextStyle? textStyle)
        {
            DrawPoint T(Circuit.Point p) => transform.Transform(new DrawPoint(p.x, p.y));

            EdgeStyle EdgeFor(Circuit.EdgeType e) => overrideEdge ?? EdgePalette.StyleFor(e);
            DrawColor? FillFor(Circuit.SymbolLayout.Shape s) =>
                (s.Fill && overrideEdge == null) ? (DrawColor?)EdgePalette.ColorFor(s.Edge) : null;

            foreach (Circuit.SymbolLayout.Shape line in layout.Lines)
                context.DrawLine(EdgeFor(line.Edge), T(line.x1), T(line.x2));

            foreach (Circuit.SymbolLayout.Shape rect in layout.Rectangles)
                context.DrawRectangle(FillFor(rect), EdgeFor(rect.Edge), new DrawRect(T(rect.x1), T(rect.x2)));

            foreach (Circuit.SymbolLayout.Shape ellipse in layout.Ellipses)
            {
                DrawPoint p1 = T(ellipse.x1);
                DrawPoint p2 = T(ellipse.x2);
                context.DrawEllipse(
                    FillFor(ellipse),
                    EdgeFor(ellipse.Edge),
                    new DrawPoint((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2),
                    (p2.X - p1.X) / 2, (p2.Y - p1.Y) / 2);
            }

            foreach (Circuit.SymbolLayout.Curve curve in layout.Curves)
            {
                IEnumerator<Circuit.Point> e = curve.x.AsEnumerable().GetEnumerator();
                if (!e.MoveNext()) continue;

                EdgeStyle edge = EdgeFor(curve.Edge);
                DrawPoint prev = T(e.Current);
                while (e.MoveNext())
                {
                    DrawPoint next = T(e.Current);
                    context.DrawLine(edge, prev, next);
                    prev = next;
                }
            }

            foreach (var arc in layout.Arcs)
            {
                // Determinant-sign flip preserves the original handedness when the
                // transform mirrors (Symbol.Flip / negative scale).
                ArcSweep sweep = (arc.Direction == Circuit.Direction.Clockwise) ^ (transform.Determinant > 0)
                    ? ArcSweep.Clockwise
                    : ArcSweep.Counterclockwise;
                bool isLargeArc = Math.Abs(arc.StartAngle - arc.EndAngle) > Math.PI;

                DrawPoint start = T(arc.Center + new Circuit.Point(Math.Cos(arc.StartAngle), Math.Sin(arc.StartAngle)) * arc.Radius);
                DrawPoint end = T(arc.Center + new Circuit.Point(Math.Cos(arc.EndAngle), Math.Sin(arc.EndAngle)) * arc.Radius);

                context.DrawArc(
                    EdgeFor(arc.Type),
                    start, end,
                    Math.Abs(arc.Radius * transform.M11),
                    Math.Abs(arc.Radius * transform.M22),
                    isLargeArc, sweep);
            }

            if (textStyle.HasValue && factory != null)
            {
                // Match the WPF code's heuristic for "y-axis scale of the current transform".
                double scale = Math.Sqrt(transform.M11 * transform.M11 + transform.M21 * transform.M21);

                foreach (Circuit.SymbolLayout.Text t in layout.Texts)
                {
                    double sizeMultiplier;
                    switch (t.Size)
                    {
                        case Circuit.Size.Small: sizeMultiplier = 0.5; break;
                        case Circuit.Size.Large: sizeMultiplier = 1.5; break;
                        default: sizeMultiplier = 1.0; break;
                    }

                    TextStyle effective = new TextStyle(
                        textStyle.Value.FontFamily,
                        textStyle.Value.FontSize * scale * sizeMultiplier,
                        textStyle.Value.FontWeight,
                        textStyle.Value.Color);

                    IFormattedText formatted = factory.CreateText(t.String, effective);

                    DrawPoint p = T(t.x);
                    DrawPoint p1 = transform.Transform(new DrawPoint(
                        t.x.x - EdgePalette.AlignmentToFactor(t.HorizontalAlign),
                        t.x.y + (1 - EdgePalette.AlignmentToFactor(t.VerticalAlign)))) - p;
                    DrawPoint p2 = transform.Transform(new DrawPoint(
                        t.x.x - (1 - EdgePalette.AlignmentToFactor(t.HorizontalAlign)),
                        t.x.y + EdgePalette.AlignmentToFactor(t.VerticalAlign))) - p;

                    double p1x = p1.X * formatted.Width;
                    double p2x = p2.X * formatted.Width;
                    double p1y = p1.Y * formatted.Height;
                    double p2y = p2.Y * formatted.Height;

                    DrawRect rc = new DrawRect(
                        Math.Min(p.X + p1x, p.X - p2x),
                        Math.Min(p.Y + p1y, p.Y - p2y),
                        formatted.Width,
                        formatted.Height);

                    context.DrawRectangle(null, TextOutlineEdge, rc);
                    context.DrawText(formatted, rc.TopLeft);
                }
            }

            // Terminals — connected ones use the wire colour, unconnected use red.
            DrawPoint dx = new DrawPoint(EdgePalette.TerminalSize / 2, EdgePalette.TerminalSize / 2);
            foreach (Circuit.Terminal term in layout.Terminals)
            {
                DrawPoint x = T(layout.MapTerminal(term));
                EdgeStyle edge = EdgePalette.StyleFor(term.ConnectedTo is null ? Circuit.EdgeType.Red : Circuit.EdgeType.Wire);
                context.DrawRectangle(edge.Color, edge, new DrawRect(x - dx, x + dx));
            }
        }
    }
}
