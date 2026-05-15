namespace SchematicControls.Editor
{
    /// <summary>
    /// Head-agnostic editor tool contract. The WPF and Avalonia heads each adapt their raw input
    /// events into <see cref="PointerEventArgs"/> / <see cref="KeyboardEventArgs"/> and dispatch
    /// them through this interface.
    ///
    /// Returning <c>true</c> from <see cref="KeyDown"/> / <see cref="KeyUp"/> means the tool
    /// handled the event and the host should mark it as handled.
    /// </summary>
    public interface IEditorTool
    {
        void Begin();
        void End();
        void Cancel();

        void MouseDown(PointerEventArgs e);
        void MouseMove(PointerEventArgs e);
        void MouseUp(PointerEventArgs e);
        void MouseDoubleClick(PointerEventArgs e);
        void MouseEnter(PointerEventArgs e);
        void MouseLeave(PointerEventArgs e);

        bool KeyDown(KeyboardEventArgs e);
        bool KeyUp(KeyboardEventArgs e);
    }
}
