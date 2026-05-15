using System.Collections.Generic;
using System.Linq;
using SchematicControls.Abstractions;
using SchematicControls.Editor.Edits;

namespace SchematicControls.Editor.Tools
{
    /// <summary>
    /// Default editing tool: click to select, drag to rubber-band, arrow keys to rotate/flip
    /// the selection, drag-on-selected to enter the MoveTool. Ported from the WPF
    /// <c>LiveSPICE.SelectionTool</c>.
    /// </summary>
    public class SelectionTool : EditorTool
    {
        private Circuit.Coord a, b;
        private bool selecting;

        public SelectionTool(ISchematicHost host) : base(host) { }

        public override void Begin()
        {
            host.SetCursor(SchematicCursor.Cross);
            host.SetSelectionRect(null, null);
        }

        public override void End()
        {
            host.SetSelectionRect(null, null);
        }

        public override void Cancel()
        {
            selecting = false;
            host.SetSelectionRect(null, null);
        }

        private bool Movable(Circuit.Coord at, KeyModifiers mods)
        {
            if ((mods & KeyModifiers.Control) != 0) return false;
            // Hit-test against current selection in element-state via ElementVisual on the host.
            // Tools don't see ElementVisual directly — fall back to checking AtPoint ∩ Selected.
            HashSet<Circuit.Element> selected = new HashSet<Circuit.Element>(host.Selected);
            return host.AtPoint(at).Any(e => selected.Contains(e));
        }

        public override void MouseDown(PointerEventArgs e)
        {
            if (Movable(e.Coord, e.Modifiers))
            {
                host.Tool = new MoveTool(host, e.Coord);
                return;
            }
            a = b = e.Coord;
            selecting = true;
            host.SetSelectionRect(a, b);
        }

        public override void MouseMove(PointerEventArgs e)
        {
            b = e.Coord;
            if (!selecting)
            {
                a = b;
                host.SetCursor(Movable(e.Coord, e.Modifiers) ? SchematicCursor.SizeAll : SchematicCursor.Cross);
            }
            else
            {
                host.SetSelectionRect(a, b);
            }
            host.Highlight(a == b ? host.AtPoint(a).Take(1) : host.InRect(a, b));
        }

        public override void MouseUp(PointerEventArgs e)
        {
            b = e.Coord;
            if (selecting)
            {
                if (a == b)
                {
                    Circuit.Element hit = host.AtPoint(a).FirstOrDefault();
                    if (hit != null) host.ToggleSelect(hit);
                    else host.Select(System.Array.Empty<Circuit.Element>());
                }
                else
                {
                    host.Select(host.InRect(a, b));
                }
                selecting = false;
                host.SetSelectionRect(null, null);
                host.SetCursor(Movable(b, e.Modifiers) ? SchematicCursor.SizeAll : SchematicCursor.Cross);
            }
        }

        public override void MouseDoubleClick(PointerEventArgs e)
        {
            // Select all symbols of the same component type as the clicked one.
            System.Type type = host.AtPoint(e.Coord).OfType<Circuit.Symbol>()
                .Select(i => i.Component.GetType()).FirstOrDefault();
            if (type != null)
                host.Select(host.Symbols.OfType<Circuit.Symbol>().Where(i => i.Component.GetType() == type), true, false);
        }

        private Circuit.Point GetSelectionCenter()
        {
            List<Circuit.Element> sel = host.Selected.ToList();
            Circuit.Coord lb = new Circuit.Coord(sel.Min(i => i.LowerBound.x), sel.Min(i => i.LowerBound.y));
            Circuit.Coord ub = new Circuit.Coord(sel.Max(i => i.UpperBound.x), sel.Max(i => i.UpperBound.y));
            Circuit.Coord mid = (lb + ub) / 2;
            return host.SnapToGrid(new Circuit.Point(mid.x, mid.y));
        }

        private void Rotate(int delta)
        {
            if (!host.Selected.Any()) return;
            host.Edits.Do(new RotateElements(host.Selected, delta, GetSelectionCenter()));
        }

        private void Flip()
        {
            if (!host.Selected.Any()) return;
            host.Edits.Do(new FlipElements(host.Selected, GetSelectionCenter().y));
        }

        public override bool KeyDown(KeyboardEventArgs e)
        {
            switch (e.Key)
            {
                case KeyCode.Left: Rotate(1); return true;
                case KeyCode.Right: Rotate(-1); return true;
                case KeyCode.Down:
                case KeyCode.Up: Flip(); return true;
                default: return base.KeyDown(e);
            }
        }
    }
}
