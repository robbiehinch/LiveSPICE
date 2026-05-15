namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Pre-measured text laid out by a head's text engine.
    /// Concrete implementations wrap WPF <c>FormattedText</c> or Avalonia <c>FormattedText</c>.
    /// </summary>
    public interface IFormattedText
    {
        double Width { get; }
        double Height { get; }
    }
}
