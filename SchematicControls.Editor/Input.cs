using System;

namespace SchematicControls.Editor
{
    [Flags]
    public enum MouseButtons
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 4,
    }

    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Windows = 8,
    }

    /// <summary>
    /// Framework-neutral key codes. Subset of the WPF <c>Key</c> / Avalonia <c>Key</c> enums
    /// covering the keys the editor tools actually inspect (arrow keys, Escape, modifiers).
    /// </summary>
    public enum KeyCode
    {
        None,
        Escape,
        Left, Right, Up, Down,
        Space,
        Enter,
        Delete,
        Tab,
        Shift, Control, Alt,
        // Letters / digits would be added on demand by tools that bind to them.
    }

    public readonly struct PointerEventArgs
    {
        /// <summary>Position in schematic (grid-snapped) coordinates.</summary>
        public Circuit.Coord Coord { get; }
        public MouseButtons Buttons { get; }
        public KeyModifiers Modifiers { get; }
        public int ClickCount { get; }

        public PointerEventArgs(Circuit.Coord coord, MouseButtons buttons, KeyModifiers modifiers, int clickCount = 1)
        {
            Coord = coord;
            Buttons = buttons;
            Modifiers = modifiers;
            ClickCount = clickCount;
        }
    }

    public readonly struct KeyboardEventArgs
    {
        public KeyCode Key { get; }
        public KeyModifiers Modifiers { get; }

        public KeyboardEventArgs(KeyCode key, KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
    }
}
