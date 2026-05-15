using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LiveSPICE.Avalonia.Controls
{
    public readonly struct ScopeTrace
    {
        public readonly string Label;
        public readonly double[] Samples;
        public readonly Color Color;

        public ScopeTrace(string label, double[] samples, Color color)
        {
            Label = label; Samples = samples ?? Array.Empty<double>(); Color = color;
        }
    }

    /// <summary>
    /// Time-domain oscilloscope. Renders multiple traces stacked on the same axes; the
    /// caller passes a fresh trace list each frame via <see cref="SetTraces"/>.
    /// </summary>
    public class ScopeView : Control
    {
        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));
        private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x48)), 0.5);
        private static readonly IPen Center = new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x68)), 0.8);

        private List<ScopeTrace> traces = new List<ScopeTrace>();
        private double range = 1.0;

        public void SetTraces(IEnumerable<ScopeTrace> next, double yRange = 1.0)
        {
            traces = next == null ? new List<ScopeTrace>() : new List<ScopeTrace>(next);
            range = Math.Max(1e-6, yRange);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect b = new Rect(Bounds.Size);
            context.FillRectangle(Background, b);

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
            context.DrawLine(Center, new Point(0, b.Height / 2), new Point(b.Width, b.Height / 2));

            double yMid = b.Height / 2;
            double yScale = b.Height / (2 * range);

            // Trace legend in the top-left corner.
            double legendY = 8;
            foreach (ScopeTrace t in traces)
            {
                DrawTrace(context, t, b, yMid, yScale);
                DrawLegend(context, t, legendY);
                legendY += 16;
            }

            // Y range indicator in the top-right corner so the user can read the current scale.
            DrawRangeLabel(context, b);
        }

        private void DrawRangeLabel(DrawingContext context, Rect b)
        {
            IBrush brush = new SolidColorBrush(Color.FromRgb(0xb0, 0xb0, 0xb0));
            FormattedText ft = new FormattedText(
                "±" + range.ToString("0.###") + " V",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 11,
                brush);
            context.DrawText(ft, new Point(b.Width - ft.Width - 8, 8));
        }

        private static void DrawTrace(DrawingContext context, ScopeTrace trace, Rect b, double yMid, double yScale)
        {
            if (trace.Samples.Length < 2 || b.Width <= 1) return;
            IPen pen = new Pen(new SolidColorBrush(trace.Color), 1.0);

            double sxScale = (double)(trace.Samples.Length - 1) / b.Width;

            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, yMid - trace.Samples[0] * yScale), false);
                int wpx = (int)b.Width;
                for (int px = 1; px < wpx; px++)
                {
                    int sampleIdx = (int)(px * sxScale);
                    if (sampleIdx >= trace.Samples.Length) break;
                    double y = yMid - trace.Samples[sampleIdx] * yScale;
                    if (double.IsNaN(y) || double.IsInfinity(y)) y = yMid;
                    ctx.LineTo(new Point(px, y));
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, pen, g);
        }

        private static void DrawLegend(DrawingContext context, ScopeTrace trace, double y)
        {
            if (string.IsNullOrEmpty(trace.Label)) return;
            IBrush brush = new SolidColorBrush(trace.Color);
            context.DrawLine(new Pen(brush, 2.0), new Point(10, y + 6), new Point(28, y + 6));
            FormattedText ft = new FormattedText(
                trace.Label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default, 11,
                brush);
            context.DrawText(ft, new Point(32, y));
        }
    }
}
