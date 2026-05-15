using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using SchematicControls.Abstractions;
using AbsColor = SchematicControls.Abstractions.DrawColor;
using AbsDashStyle = SchematicControls.Abstractions.DashStyle;
using AbsLineCap = SchematicControls.Abstractions.LineCap;
using AbsPoint = SchematicControls.Abstractions.DrawPoint;
using AbsRect = SchematicControls.Abstractions.DrawRect;
using AbsMatrix = SchematicControls.Abstractions.DrawMatrix;

namespace SchematicControls.Wpf
{
    internal static class WpfConversions
    {
        public static Color ToWpf(AbsColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        public static Brush ToBrush(AbsColor c)
        {
            // Avoid allocating a new brush on every paint.
            if (!brushCache.TryGetValue(c, out Brush b))
            {
                SolidColorBrush scb = new SolidColorBrush(ToWpf(c));
                scb.Freeze();
                b = scb;
                brushCache[c] = b;
            }
            return b;
        }

        public static Pen ToPen(EdgeStyle e)
        {
            // Pens carry brush + thickness + dash + caps. Cache so frozen Pens get reused.
            PenKey key = new PenKey(e.Color, e.Thickness, e.Dash, e.LineCap);
            if (!penCache.TryGetValue(key, out Pen p))
            {
                p = new Pen(ToBrush(e.Color), e.Thickness)
                {
                    DashStyle = e.Dash == AbsDashStyle.Dashed ? DashStyles.Dash : DashStyles.Solid,
                    StartLineCap = e.LineCap == AbsLineCap.Round ? PenLineCap.Round : PenLineCap.Flat,
                    EndLineCap = e.LineCap == AbsLineCap.Round ? PenLineCap.Round : PenLineCap.Flat,
                };
                p.Freeze();
                penCache[key] = p;
            }
            return p;
        }

        public static Point ToPoint(AbsPoint p) => new Point(p.X, p.Y);
        public static Rect ToRect(AbsRect r) => new Rect(r.X, r.Y, r.Width, r.Height);

        public static Matrix ToMatrix(AbsMatrix m) => new Matrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);

        public static AbsMatrix FromMatrix(Matrix m) => new AbsMatrix
        {
            M11 = m.M11, M12 = m.M12,
            M21 = m.M21, M22 = m.M22,
            OffsetX = m.OffsetX, OffsetY = m.OffsetY,
        };

        private static readonly Dictionary<AbsColor, Brush> brushCache = new Dictionary<AbsColor, Brush>();
        private static readonly Dictionary<PenKey, Pen> penCache = new Dictionary<PenKey, Pen>();

        private readonly struct PenKey
        {
            public readonly AbsColor Color;
            public readonly double Thickness;
            public readonly AbsDashStyle Dash;
            public readonly AbsLineCap Cap;
            public PenKey(AbsColor c, double t, AbsDashStyle d, AbsLineCap cap) { Color = c; Thickness = t; Dash = d; Cap = cap; }
            public override bool Equals(object obj) => obj is PenKey k && k.Color.Equals(Color) && k.Thickness == Thickness && k.Dash == Dash && k.Cap == Cap;
            public override int GetHashCode() => Color.GetHashCode() ^ Thickness.GetHashCode() ^ ((int)Dash << 4) ^ ((int)Cap << 8);
        }
    }
}
