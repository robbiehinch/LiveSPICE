using Avalonia;
using Avalonia.Media;
using SchematicControls.Abstractions;
using AbsFontWeight = SchematicControls.Abstractions.FontWeight;

namespace LiveSPICE.Avalonia.Drawing
{
    public sealed class AvaloniaFormattedText : IFormattedText
    {
        public FormattedText Native { get; }

        public AvaloniaFormattedText(FormattedText native)
        {
            Native = native;
        }

        public double Width => Native.Width;
        public double Height => Native.Height;
    }

    public sealed class AvaloniaDrawingContextFactory : IDrawingContextFactory
    {
        public IFormattedText CreateText(string text, TextStyle style)
        {
            FormattedText ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily(style.FontFamily),
                    FontStyle.Normal,
                    style.FontWeight == AbsFontWeight.Bold ? global::Avalonia.Media.FontWeight.Bold : global::Avalonia.Media.FontWeight.Normal),
                style.FontSize,
                new SolidColorBrush(Color.FromArgb(style.Color.A, style.Color.R, style.Color.G, style.Color.B)));
            return new AvaloniaFormattedText(ft);
        }
    }
}
