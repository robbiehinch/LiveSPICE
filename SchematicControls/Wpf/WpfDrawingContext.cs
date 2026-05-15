using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using SchematicControls.Abstractions;
using DrawColorAbs = SchematicControls.Abstractions.DrawColor;
using DrawPointAbs = SchematicControls.Abstractions.DrawPoint;
using DrawRectAbs = SchematicControls.Abstractions.DrawRect;
using DrawMatrixAbs = SchematicControls.Abstractions.DrawMatrix;

namespace SchematicControls.Wpf
{
    /// <summary>
    /// Wraps a WPF <see cref="DrawingContext"/> so head-agnostic renderers can target it.
    /// </summary>
    public sealed class WpfDrawingContext : IDrawingContext
    {
        private readonly DrawingContext dc;
        private int pushedTransforms;

        public WpfDrawingContext(DrawingContext dc)
        {
            this.dc = dc;
        }

        public void DrawLine(EdgeStyle edge, DrawPointAbs p1, DrawPointAbs p2)
        {
            dc.DrawLine(WpfConversions.ToPen(edge), WpfConversions.ToPoint(p1), WpfConversions.ToPoint(p2));
        }

        public void DrawRectangle(DrawColorAbs? fill, EdgeStyle? edge, DrawRectAbs rect)
        {
            Brush brush = fill.HasValue ? WpfConversions.ToBrush(fill.Value) : null;
            Pen pen = edge.HasValue ? WpfConversions.ToPen(edge.Value) : null;
            dc.DrawRectangle(brush, pen, WpfConversions.ToRect(rect));
        }

        public void DrawEllipse(DrawColorAbs? fill, EdgeStyle? edge, DrawPointAbs center, double radiusX, double radiusY)
        {
            Brush brush = fill.HasValue ? WpfConversions.ToBrush(fill.Value) : null;
            Pen pen = edge.HasValue ? WpfConversions.ToPen(edge.Value) : null;
            dc.DrawEllipse(brush, pen, WpfConversions.ToPoint(center), radiusX, radiusY);
        }

        public void DrawArc(EdgeStyle edge, DrawPointAbs start, DrawPointAbs end,
            double radiusX, double radiusY, bool isLargeArc, ArcSweep sweep)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(WpfConversions.ToPoint(start), false, false);
                ctx.ArcTo(
                    WpfConversions.ToPoint(end),
                    new Size(radiusX, radiusY),
                    0,
                    isLargeArc,
                    sweep == ArcSweep.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                    true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, WpfConversions.ToPen(edge), g);
        }

        public void DrawText(IFormattedText text, DrawPointAbs origin)
        {
            if (text is WpfFormattedText w)
                dc.DrawText(w.Native, WpfConversions.ToPoint(origin));
        }

        public void PushTransform(DrawMatrixAbs transform)
        {
            dc.PushTransform(new MatrixTransform(WpfConversions.ToMatrix(transform)));
            pushedTransforms++;
        }

        public void Pop()
        {
            if (pushedTransforms > 0)
            {
                dc.Pop();
                pushedTransforms--;
            }
        }
    }
}
