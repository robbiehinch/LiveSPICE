using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SchematicControls.Abstractions;
using AbsFontWeight = SchematicControls.Abstractions.FontWeight;

namespace SchematicControls.Wpf
{
    /// <summary>
    /// Adapter exposing WPF <see cref="FormattedText"/> through the head-agnostic
    /// <see cref="IFormattedText"/> contract.
    /// </summary>
    public sealed class WpfFormattedText : IFormattedText
    {
        public FormattedText Native { get; }

        public WpfFormattedText(FormattedText native)
        {
            Native = native;
        }

        public double Width => Native.Width;
        public double Height => Native.Height;
    }

    public sealed class WpfDrawingContextFactory : IDrawingContextFactory
    {
        private readonly double pixelsPerDip;

        public WpfDrawingContextFactory(double pixelsPerDip)
        {
            this.pixelsPerDip = pixelsPerDip;
        }

        public IFormattedText CreateText(string text, TextStyle style)
        {
            FormattedText ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily(style.FontFamily),
                    FontStyles.Normal,
                    style.FontWeight == AbsFontWeight.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal),
                style.FontSize,
                WpfConversions.ToBrush(style.Color),
                pixelsPerDip);
            return new WpfFormattedText(ft);
        }
    }
}
