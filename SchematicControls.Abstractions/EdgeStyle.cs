namespace SchematicControls.Abstractions
{
    public enum DashStyle { Solid, Dashed }
    public enum LineCap { Flat, Round }
    public enum ArcSweep { Clockwise, Counterclockwise }
    public enum FontWeight { Normal, Bold }

    /// <summary>
    /// Edge (stroke) properties for a drawn primitive. Replaces WPF <c>Pen</c> in head-agnostic code.
    /// </summary>
    public readonly struct EdgeStyle
    {
        public readonly DrawColor Color;
        public readonly double Thickness;
        public readonly DashStyle Dash;
        public readonly LineCap LineCap;

        public EdgeStyle(DrawColor color, double thickness, DashStyle dash = DashStyle.Solid, LineCap lineCap = LineCap.Round)
        {
            Color = color;
            Thickness = thickness;
            Dash = dash;
            LineCap = lineCap;
        }
    }

    public readonly struct TextStyle
    {
        public readonly string FontFamily;
        public readonly double FontSize;
        public readonly FontWeight FontWeight;
        public readonly DrawColor Color;

        public TextStyle(string fontFamily, double fontSize, FontWeight fontWeight, DrawColor color)
        {
            FontFamily = fontFamily;
            FontSize = fontSize;
            FontWeight = fontWeight;
            Color = color;
        }
    }
}
