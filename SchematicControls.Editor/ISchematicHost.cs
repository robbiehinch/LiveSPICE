using System;
using System.Collections.Generic;
using SchematicControls.Editor.Edits;

namespace SchematicControls.Editor
{
    /// <summary>
    /// Host-side surface that editor tools call back into. Implemented by each UI head's
    /// schematic control. Decouples tools from any specific framework (WPF, Avalonia).
    /// </summary>
    public interface ISchematicHost
    {
        Circuit.Schematic Schematic { get; }

        /// <summary>Snap a free-coord position to the schematic grid.</summary>
        Circuit.Coord SnapToGrid(Circuit.Point p);

        IEnumerable<Circuit.Element> Elements { get; }
        IEnumerable<Circuit.Element> Selected { get; }
        IEnumerable<Circuit.Element> Symbols { get; }
        IEnumerable<Circuit.Wire> Wires { get; }

        IEnumerable<Circuit.Element> AtPoint(Circuit.Coord at);
        IEnumerable<Circuit.Element> InRect(Circuit.Coord a, Circuit.Coord b);

        void Select(IEnumerable<Circuit.Element> elements, bool replace = true, bool toggle = false);
        void ToggleSelect(Circuit.Element element);
        void Highlight(IEnumerable<Circuit.Element> elements);

        void Add(IEnumerable<Circuit.Element> elements);
        void Remove(IEnumerable<Circuit.Element> elements);

        /// <summary>Undo/redo stack. Tools call <c>Edits.Do(...)</c> to push and execute edits.</summary>
        EditStack Edits { get; }

        /// <summary>Open an atomic undo group; pair with <see cref="EndEditGroup"/>.</summary>
        void BeginEditGroup();
        void EndEditGroup();

        /// <summary>Replace the active tool. The host calls <c>End</c> on the outgoing tool and <c>Begin</c> on the incoming.</summary>
        IEditorTool Tool { get; set; }

        /// <summary>Set the on-screen cursor (resolved per head: WPF <c>Cursor</c>, Avalonia <c>StandardCursorType</c>, etc.).</summary>
        void SetCursor(SchematicCursor cursor);

        /// <summary>Show/hide and update a host-managed rubber-band selection rectangle in schematic coords.</summary>
        void SetSelectionRect(Circuit.Coord? a, Circuit.Coord? b);

        /// <summary>Show/hide a host-rendered "ghost" symbol (used by SymbolTool during placement).</summary>
        void SetGhostSymbol(Circuit.Symbol symbol);

        /// <summary>Show/hide a host-rendered wire-path preview (used by WireTool during draw).</summary>
        void SetWirePathPreview(IReadOnlyList<Circuit.Coord> path);

        /// <summary>Build a single wire between two grid-aligned points, merging coincident wires and splitting at terminals.</summary>
        void AddWire(Circuit.Coord a, Circuit.Coord b);

        /// <summary>Build a multi-segment wire path (each consecutive pair becomes one straight wire run).</summary>
        void AddWire(IList<Circuit.Coord> points);

        /// <summary>Compute the best L-shaped wire path between the first and last point of <paramref name="mouseTrail"/>.</summary>
        List<Circuit.Coord> FindWirePath(List<Circuit.Coord> mouseTrail);
    }

    public enum SchematicCursor
    {
        Default,
        Cross,
        SizeAll,
        Hand,
        Pen,
        None,
    }
}
