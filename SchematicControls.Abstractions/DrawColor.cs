namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Framework-agnostic ARGB color. Each head adapts this to its native color type
    /// (WPF <c>System.Windows.Media.Color</c>, Avalonia <c>Avalonia.Media.Color</c>, etc.).
    /// </summary>
    public readonly struct DrawColor
    {
        public readonly byte A;
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public DrawColor(byte a, byte r, byte g, byte b)
        {
            A = a; R = r; G = g; B = b;
        }

        public static DrawColor FromArgb(byte a, byte r, byte g, byte b) => new DrawColor(a, r, g, b);
        public static DrawColor FromRgb(byte r, byte g, byte b) => new DrawColor(255, r, g, b);

        public uint ToUInt32() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

        public static readonly DrawColor Transparent = new DrawColor(0, 0, 0, 0);
        public static readonly DrawColor Black = FromRgb(0, 0, 0);
        public static readonly DrawColor Gray = FromRgb(128, 128, 128);
        public static readonly DrawColor DarkBlue = FromRgb(0, 0, 139);
        public static readonly DrawColor DodgerBlue = FromRgb(30, 144, 255);
        public static readonly DrawColor Red = FromRgb(255, 0, 0);
        public static readonly DrawColor Lime = FromRgb(0, 255, 0);
        public static readonly DrawColor Blue = FromRgb(0, 0, 255);
        public static readonly DrawColor Yellow = FromRgb(255, 255, 0);
        public static readonly DrawColor Cyan = FromRgb(0, 255, 255);
        public static readonly DrawColor Magenta = FromRgb(255, 0, 255);
        public static readonly DrawColor Orange = FromRgb(255, 165, 0);

        public override bool Equals(object obj) => obj is DrawColor c && c.A == A && c.R == R && c.G == G && c.B == B;
        public override int GetHashCode() => unchecked((int)ToUInt32());
    }
}
