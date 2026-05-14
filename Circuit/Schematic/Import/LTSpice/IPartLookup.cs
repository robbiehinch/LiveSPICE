namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// Looks up a Circuit.Component template by SPICE part number (e.g. "1N4148", "2N3904", "LM741").
    /// The returned Component is a template; the caller is responsible for cloning if needed.
    /// </summary>
    public interface IPartLookup
    {
        /// <summary>
        /// Find a component whose PartNumber matches the given name (case-insensitive).
        /// Returns null if no match.
        /// </summary>
        Component TryGetByPartNumber(string partNumber);
    }

    /// <summary>
    /// A no-op part lookup that always returns null.
    /// </summary>
    public sealed class NullPartLookup : IPartLookup
    {
        public static readonly NullPartLookup Instance = new NullPartLookup();
        public Component TryGetByPartNumber(string partNumber) { return null; }
    }
}
