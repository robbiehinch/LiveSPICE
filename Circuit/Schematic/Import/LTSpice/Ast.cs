using System.Collections.Generic;

namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// LTSpice orientation: R0-R270 are rotations, M0-M270 are rotations plus a horizontal mirror.
    /// </summary>
    public enum LTOrientation
    {
        R0,
        R90,
        R180,
        R270,
        M0,
        M90,
        M180,
        M270,
    }

    /// <summary>
    /// A SYMBOL line in the .asc file plus its trailing SYMATTR / WINDOW records.
    /// </summary>
    public class LTSymbol
    {
        public int LineNumber;
        public string SymbolName;       // "res", "diode", "Opamps\\opamp", ...
        public int X;                   // LTSpice anchor x (16-grid)
        public int Y;                   // LTSpice anchor y (16-grid)
        public LTOrientation Orientation;
        public Dictionary<string, string> Attributes = new Dictionary<string, string>();
    }

    /// <summary>
    /// A WIRE record: two endpoints in LTSpice coordinates.
    /// </summary>
    public class LTWire
    {
        public int LineNumber;
        public int X1, Y1, X2, Y2;
    }

    /// <summary>
    /// A FLAG record: a marker at (X, Y) that names that net.
    /// </summary>
    public class LTFlag
    {
        public int LineNumber;
        public int X, Y;
        public string Name;             // "0" means ground.
    }

    /// <summary>
    /// A TEXT record: SPICE directive (starts with !) or comment (starts with ;) preserved as a Label.
    /// </summary>
    public class LTText
    {
        public int LineNumber;
        public int X, Y;
        public string Content;
        public bool IsSpiceDirective;   // true if starts with '!'
    }

    /// <summary>
    /// The whole parsed schematic.
    /// </summary>
    public class LTSpiceSheet
    {
        public int Version = 4;
        public List<LTSymbol> Symbols = new List<LTSymbol>();
        public List<LTWire> Wires = new List<LTWire>();
        public List<LTFlag> Flags = new List<LTFlag>();
        public List<LTText> Texts = new List<LTText>();
    }
}
