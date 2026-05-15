using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LiveSPICE.Avalonia.Controls
{
    /// <summary>
    /// Simple time-domain oscilloscope display. The host pushes samples via <see cref="SetTrace"/>
    /// and calls <c>InvalidateVisual</c> (or the embedded refresh timer can be used).
    /// Phase 5 only — frequency-domain analysis, multi-trace, and tunable-pitch markers come later.
    /// </summary>
    public class ScopeView : Control
    {
        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));
        private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x48)), 0.5);
        private static readonly IPen Center = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x68)), 0.8);
        private static readonly IPen TracePen = new Pen(new SolidColorBrush(Color.FromRgb(0x4f, 0xc1, 0xff)), 1.0);

        private double[] trace = Array.Empty<double>();
        private double range = 1.0;

        public void SetTrace(double[] samples, double yRange = 1.0)
        {
            trace = samples ?? Array.Empty<double>();
            range = Math.Max(1e-6, yRange);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect b = new Rect(Bounds.Size);
            context.FillRectangle(Background, b);

            // Grid: 8 horizontal, 10 vertical divisions
            for (int i = 1; i < 8; i++)
            {
                double y = b.Height * i / 8.0;
                context.DrawLine(Grid, new Point(0, y), new Point(b.Width, y));
            }
            for (int i = 1; i < 10; i++)
            {
                double x = b.Width * i / 10.0;
                context.DrawLine(Grid, new Point(x, 0), new Point(x, b.Height));
            }
            // Centerline
            context.DrawLine(Center, new Point(0, b.Height / 2), new Point(b.Width, b.Height / 2));

            if (trace.Length < 2 || b.Width <= 1) return;

            // Stride samples to width pixels.
            double sxScale = (double)(trace.Length - 1) / b.Width;
            double yMid = b.Height / 2;
            double yScale = b.Height / (2 * range);

            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, yMid - trace[0] * yScale), false);
                int wpx = (int)b.Width;
                for (int px = 1; px < wpx; px++)
                {
                    int sampleIdx = (int)(px * sxScale);
                    if (sampleIdx >= trace.Length) break;
                    double y = yMid - trace[sampleIdx] * yScale;
                    if (double.IsNaN(y) || double.IsInfinity(y)) y = yMid;
                    ctx.LineTo(new Point(px, y));
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, TracePen, g);
        }
    }
}
