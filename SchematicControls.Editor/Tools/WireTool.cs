using System.Collections.Generic;

namespace SchematicControls.Editor.Tools
{
    /// <summary>Draws an L-shaped wire from mouse-down to mouse-up. Ctrl on mouse-up keeps the tool active for chaining wires.</summary>
    public class WireTool : EditorTool
    {
        private List<Circuit.Coord> trail;

        public WireTool(ISchematicHost host) : base(host) { }

        public override void Begin() => host.SetCursor(SchematicCursor.Pen);
        public override void End() => host.SetWirePathPreview(null);

        public override void MouseDown(PointerEventArgs e)
        {
            trail = new List<Circuit.Coord> { e.Coord };
            host.SetWirePathPreview(host.FindWirePath(trail));
        }

        public override void MouseMove(PointerEventArgs e)
        {
            if (trail == null) return;
            trail.Add(e.Coord);
            host.SetWirePathPreview(host.FindWirePath(trail));
        }

        public override void MouseUp(PointerEventArgs e)
        {
            host.SetWirePathPreview(null);
            if (trail != null)
            {
                List<Circuit.Coord> path = host.FindWirePath(trail);
                host.AddWire(path);
                trail = null;
            }
            if ((e.Modifiers & KeyModifiers.Control) == 0)
                host.Tool = new SelectionTool(host);
        }
    }
}
