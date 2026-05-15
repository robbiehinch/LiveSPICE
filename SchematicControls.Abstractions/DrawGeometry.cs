using System;

namespace SchematicControls.Abstractions
{
    public readonly struct DrawPoint
    {
        public readonly double X;
        public readonly double Y;
        public DrawPoint(double x, double y) { X = x; Y = y; }

        public static DrawPoint operator +(DrawPoint a, DrawPoint b) => new DrawPoint(a.X + b.X, a.Y + b.Y);
        public static DrawPoint operator -(DrawPoint a, DrawPoint b) => new DrawPoint(a.X - b.X, a.Y - b.Y);
    }

    public readonly struct DrawRect
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public DrawRect(double x, double y, double width, double height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public DrawRect(DrawPoint a, DrawPoint b)
        {
            X = Math.Min(a.X, b.X);
            Y = Math.Min(a.Y, b.Y);
            Width = Math.Abs(b.X - a.X);
            Height = Math.Abs(b.Y - a.Y);
        }

        public DrawPoint TopLeft => new DrawPoint(X, Y);
        public DrawPoint BottomRight => new DrawPoint(X + Width, Y + Height);
        public DrawPoint Center => new DrawPoint(X + Width / 2, Y + Height / 2);
    }

    /// <summary>
    /// 2D affine transform. Matches the column-vector convention of <c>System.Windows.Media.Matrix</c>:
    /// <c>(x, y, 1) * M = (x', y', 1)</c> where <c>M = [[M11,M12,0],[M21,M22,0],[OffsetX,OffsetY,1]]</c>.
    /// </summary>
    public struct DrawMatrix
    {
        public double M11, M12, M21, M22, OffsetX, OffsetY;

        public static DrawMatrix Identity => new DrawMatrix { M11 = 1, M22 = 1 };

        public DrawPoint Transform(DrawPoint p) => new DrawPoint(
            p.X * M11 + p.Y * M21 + OffsetX,
            p.X * M12 + p.Y * M22 + OffsetY);

        public double Determinant => M11 * M22 - M12 * M21;

        public void Translate(double dx, double dy) { OffsetX += dx; OffsetY += dy; }

        public void Scale(double sx, double sy)
        {
            M11 *= sx; M12 *= sx;
            M21 *= sy; M22 *= sy;
            OffsetX *= sx; OffsetY *= sy;
        }

        public void Rotate(double angleDegrees)
        {
            double r = angleDegrees * Math.PI / 180.0;
            double c = Math.Cos(r), s = Math.Sin(r);
            double nm11 = M11 * c - M12 * s;
            double nm12 = M11 * s + M12 * c;
            double nm21 = M21 * c - M22 * s;
            double nm22 = M21 * s + M22 * c;
            double nox = OffsetX * c - OffsetY * s;
            double noy = OffsetX * s + OffsetY * c;
            M11 = nm11; M12 = nm12; M21 = nm21; M22 = nm22;
            OffsetX = nox; OffsetY = noy;
        }
    }
}
