namespace SchematicControls.Editor.Tools
{
    /// <summary>
    /// Base class for the concrete editor tools. Holds the host reference and provides
    /// the common Escape→SelectionTool behaviour.
    /// </summary>
    public abstract class EditorTool : IEditorTool
    {
        protected readonly ISchematicHost host;

        protected EditorTool(ISchematicHost host) { this.host = host; }

        public virtual void Begin() { }
        public virtual void End() { }
        public virtual void Cancel() { }

        public virtual void MouseDown(PointerEventArgs e) { }
        public virtual void MouseMove(PointerEventArgs e) { }
        public virtual void MouseUp(PointerEventArgs e) { }
        public virtual void MouseDoubleClick(PointerEventArgs e) { }
        public virtual void MouseEnter(PointerEventArgs e) { }
        public virtual void MouseLeave(PointerEventArgs e) { }

        public virtual bool KeyDown(KeyboardEventArgs e)
        {
            if (e.Key == KeyCode.Escape)
            {
                host.Tool = new SelectionTool(host);
                return true;
            }
            return false;
        }

        public virtual bool KeyUp(KeyboardEventArgs e) => false;
    }
}
