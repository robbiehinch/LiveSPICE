using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvDc = Avalonia.Media.DrawingContext;
using AvColor = Avalonia.Media.Color;
using AvPen = Avalonia.Media.Pen;
using AvBrush = Avalonia.Media.IBrush;
using AvPoint = Avalonia.Point;
using AvRect = Avalonia.Rect;
using AvMatrix = Avalonia.Matrix;
using AvStreamGeometry = Avalonia.Media.StreamGeometry;
using AvSweepDirection = Avalonia.Media.SweepDirection;
using AvSolidColorBrush = Avalonia.Media.SolidColorBrush;
using AvDashStyle = Avalonia.Media.DashStyle;
using AvIDashStyle = Avalonia.Media.IDashStyle;
using AvLineCap = Avalonia.Media.PenLineCap;
using SchematicControls.Abstractions;
using AbsColor = SchematicControls.Abstractions.DrawColor;
using AbsPoint = SchematicControls.Abstractions.DrawPoint;
using AbsRect = SchematicControls.Abstractions.DrawRect;
using AbsMatrix = SchematicControls.Abstractions.DrawMatrix;
using AbsDashStyle = SchematicControls.Abstractions.DashStyle;
using AbsLineCap = SchematicControls.Abstractions.LineCap;

namespace LiveSPICE.Avalonia.Drawing
{
    /// <summary>
    /// Adapts Avalonia's <see cref="AvDc"/> to the head-agnostic
    /// <see cref="IDrawingContext"/>. The pair to <c>WpfDrawingContext</c> in the WPF head.
    /// </summary>
    public sealed class AvaloniaDrawingContext : IDrawingContext
    {
        private readonly AvDc dc;
        private readonly Stack<AvDc.PushedState> pushed = new Stack<AvDc.PushedState>();

        public AvaloniaDrawingContext(AvDc dc)
        {
            this.dc = dc;
        }

        public void DrawLine(EdgeStyle edge, AbsPoint p1, AbsPoint p2)
        {
            dc.DrawLine(ToPen(edge), ToPoint(p1), ToPoint(p2));
        }

        public void DrawRectangle(AbsColor? fill, EdgeStyle? edge, AbsRect rect)
        {
            AvBrush brush = fill.HasValue ? ToBrush(fill.Value) : null;
            AvPen pen = edge.HasValue ? ToPen(edge.Value) : null;
            dc.DrawRectangle(brush, pen, ToRect(rect));
        }

        public void DrawEllipse(AbsColor? fill, EdgeStyle? edge, AbsPoint center, double radiusX, double radiusY)
        {
            AvBrush brush = fill.HasValue ? ToBrush(fill.Value) : null;
            AvPen pen = edge.HasValue ? ToPen(edge.Value) : null;
            dc.DrawEllipse(brush, pen, ToPoint(center), radiusX, radiusY);
        }

        public void DrawArc(EdgeStyle edge, AbsPoint start, AbsPoint end,
            double radiusX, double radiusY, bool isLargeArc, ArcSweep sweep)
        {
            AvStreamGeometry g = new AvStreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(ToPoint(start), false);
                ctx.ArcTo(
                    ToPoint(end),
                    new Size(radiusX, radiusY),
                    0,
                    isLargeArc,
                    sweep == ArcSweep.Clockwise ? AvSweepDirection.Clockwise : AvSweepDirection.CounterClockwise);
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, ToPen(edge), g);
        }

        public void DrawText(IFormattedText text, AbsPoint origin)
        {
            if (text is AvaloniaFormattedText a)
                dc.DrawText(a.Native, ToPoint(origin));
        }

        public void PushTransform(AbsMatrix transform)
        {
            pushed.Push(dc.PushTransform(ToMatrix(transform)));
        }

        public void Pop()
        {
            if (pushed.Count > 0)
                pushed.Pop().Dispose();
        }

        // ---- Conversions ----

        private static AvPoint ToPoint(AbsPoint p) => new AvPoint(p.X, p.Y);
        private static AvRect ToRect(AbsRect r) => new AvRect(r.X, r.Y, r.Width, r.Height);
        private static AvColor ToColor(AbsColor c) => AvColor.FromArgb(c.A, c.R, c.G, c.B);
        private static AvBrush ToBrush(AbsColor c) => new AvSolidColorBrush(ToColor(c));

        private static AvMatrix ToMatrix(AbsMatrix m) =>
            new AvMatrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);

        private static AvPen ToPen(EdgeStyle e)
        {
            AvIDashStyle dash = e.Dash == AbsDashStyle.Dashed ? AvDashStyle.Dash : null;
            AvLineCap cap = e.LineCap == AbsLineCap.Round ? AvLineCap.Round : AvLineCap.Flat;
            return new AvPen(ToBrush(e.Color), e.Thickness)
            {
                DashStyle = dash,
                LineCap = cap,
            };
        }
    }
}
