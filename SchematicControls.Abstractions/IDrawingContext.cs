namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Head-agnostic 2D drawing surface. Each UI framework (WPF, Avalonia, ...) provides
    /// its own adapter that translates these calls into the native drawing API.
    /// </summary>
    public interface IDrawingContext
    {
        void DrawLine(EdgeStyle edge, DrawPoint p1, DrawPoint p2);

        void DrawRectangle(DrawColor? fill, EdgeStyle? edge, DrawRect rect);

        void DrawEllipse(DrawColor? fill, EdgeStyle? edge, DrawPoint center, double radiusX, double radiusY);

        /// <summary>
        /// Stroke (no fill) an elliptical arc from <paramref name="start"/> to <paramref name="end"/>.
        /// </summary>
        void DrawArc(EdgeStyle edge, DrawPoint start, DrawPoint end,
            double radiusX, double radiusY, bool isLargeArc, ArcSweep sweep);

        void DrawText(IFormattedText text, DrawPoint origin);

        /// <summary>Pushes a transform onto the context's stack. Pair with <see cref="Pop"/>.</summary>
        void PushTransform(DrawMatrix transform);

        /// <summary>Pops the last <see cref="PushTransform"/>.</summary>
        void Pop();
    }

    /// <summary>
    /// Head-specific factory for objects that can only be created against a live UI thread
    /// (text measurement, dpi scaling, etc.).
    /// </summary>
    public interface IDrawingContextFactory
    {
        IFormattedText CreateText(string text, TextStyle style);
    }
}
