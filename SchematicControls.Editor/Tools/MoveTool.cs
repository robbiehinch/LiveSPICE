using SchematicControls.Editor.Edits;

namespace SchematicControls.Editor.Tools
{
    /// <summary>Tool for moving the current selection by mouse drag. Auto-returns to SelectionTool on mouse-up.</summary>
    public class MoveTool : EditorTool
    {
        private Circuit.Coord x;

        public MoveTool(ISchematicHost host, Circuit.Coord at) : base(host) { x = at; }

        public override void Begin()
        {
            host.BeginEditGroup();
            host.SetCursor(SchematicCursor.SizeAll);
        }

        public override void End() => host.EndEditGroup();

        public override void Cancel()
        {
            // Cancel discards in-flight movement; next selection-tool gets a fresh group.
            host.EndEditGroup();
            host.BeginEditGroup();
        }

        public override void MouseUp(PointerEventArgs e)
        {
            host.Tool = new SelectionTool(host);
        }

        public override void MouseMove(PointerEventArgs e)
        {
            Circuit.Coord dx = e.Coord - x;
            if (dx.x != 0 || dx.y != 0)
            {
                if (host.Selected.GetEnumerator().MoveNext())
                    host.Edits.Do(new MoveElements(host.Selected, dx));
            }
            x = e.Coord;
        }
    }
}
