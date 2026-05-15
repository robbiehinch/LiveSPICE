using System;

namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Maps <see cref="Circuit.EdgeType"/> to <see cref="DrawColor"/> and standard <see cref="EdgeStyle"/>
    /// values. Both UI heads share this palette so colours stay identical.
    /// </summary>
    public static class EdgePalette
    {
        public const double EdgeThickness = 1.0;
        public const double TerminalSize = 2.0;
        public const double WireTerminalSize = TerminalSize * 0.9;

        public static DrawColor WireColor { get; set; } = DrawColor.DarkBlue;

        public static DrawColor ColorFor(Circuit.EdgeType edge)
        {
            switch (edge)
            {
                case Circuit.EdgeType.Wire: return WireColor;
                case Circuit.EdgeType.Black: return DrawColor.Black;
                case Circuit.EdgeType.Gray: return DrawColor.Gray;
                case Circuit.EdgeType.Red: return DrawColor.Red;
                case Circuit.EdgeType.Green: return DrawColor.Lime;
                case Circuit.EdgeType.Blue: return DrawColor.Blue;
                case Circuit.EdgeType.Yellow: return DrawColor.Yellow;
                case Circuit.EdgeType.Cyan: return DrawColor.Cyan;
                case Circuit.EdgeType.Magenta: return DrawColor.Magenta;
                case Circuit.EdgeType.Orange: return DrawColor.Orange;
                default: throw new ArgumentException("Unknown edge type: " + edge);
            }
        }

        public static EdgeStyle StyleFor(Circuit.EdgeType edge) =>
            new EdgeStyle(ColorFor(edge), EdgeThickness);

        public static double AlignmentToFactor(Circuit.Alignment a)
        {
            switch (a)
            {
                case Circuit.Alignment.Near: return 0.0;
                case Circuit.Alignment.Center: return 0.5;
                case Circuit.Alignment.Far: return 1.0;
                default: throw new ArgumentException("Unknown alignment: " + a);
            }
        }
    }
}
