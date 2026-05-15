using SchematicControls.Editor.Edits;

namespace SchematicControls.Editor.Tools
{
    /// <summary>
    /// Drops a copy of a chosen <see cref="Circuit.Component"/> at the mouse position.
    /// Held active for chained placement on Ctrl-click; arrow keys rotate / flip the
    /// ghost preview before placement.
    /// </summary>
    public class SymbolTool : EditorTool
    {
        private readonly Circuit.Component prototype;
        private Circuit.Symbol ghost;

        public SymbolTool(ISchematicHost host, Circuit.Component component) : base(host)
        {
            prototype = component;
        }

        public override void Begin()
        {
            ghost = new Circuit.Symbol(prototype.Clone());
            host.SetCursor(SchematicCursor.None);
            host.SetGhostSymbol(ghost);
        }

        public override void End() => host.SetGhostSymbol(null);

        public override void MouseMove(PointerEventArgs e)
        {
            if (ghost == null) return;
            ghost.Position = e.Coord;
            host.SetGhostSymbol(ghost);
        }

        public override void MouseLeave(PointerEventArgs e) => host.SetGhostSymbol(null);
        public override void MouseEnter(PointerEventArgs e) => host.SetGhostSymbol(ghost);

        public override void MouseDown(PointerEventArgs e)
        {
            if (ghost == null) return;
            Circuit.Symbol placed = new Circuit.Symbol(ghost.Component.Clone())
            {
                Position = ghost.Position,
                Rotation = ghost.Rotation,
                Flip = ghost.Flip,
            };
            host.Edits.Do(new AddElements(host.Schematic, new Circuit.Element[] { placed }));
            host.Select(new Circuit.Element[] { placed }, true, false);

            if ((e.Modifiers & KeyModifiers.Control) == 0)
                host.Tool = new SelectionTool(host);
        }

        public override bool KeyDown(KeyboardEventArgs e)
        {
            if (ghost == null) return base.KeyDown(e);
            switch (e.Key)
            {
                case KeyCode.Left: ghost.Rotation += 1; host.SetGhostSymbol(ghost); return true;
                case KeyCode.Right: ghost.Rotation -= 1; host.SetGhostSymbol(ghost); return true;
                case KeyCode.Down:
                case KeyCode.Up: ghost.Flip = !ghost.Flip; host.SetGhostSymbol(ghost); return true;
                default: return base.KeyDown(e);
            }
        }
    }
}
