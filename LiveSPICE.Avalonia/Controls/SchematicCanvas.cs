using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LiveSPICE.Avalonia.Drawing;
using SchematicControls.Abstractions;
using SchematicControls.Editor;
using SchematicControls.Editor.Edits;
using SchematicControls.Editor.Tools;
using AvCursor = Avalonia.Input.Cursor;
using AvKeyModifiers = Avalonia.Input.KeyModifiers;
using AvPointerEventArgs = Avalonia.Input.PointerEventArgs;
using KeyModifiers = SchematicControls.Editor.KeyModifiers;
using PointerEventArgs = SchematicControls.Editor.PointerEventArgs;

namespace LiveSPICE.Avalonia.Controls
{
    /// <summary>
    /// Avalonia <see cref="Control"/> hosting a <see cref="Circuit.Schematic"/> for both
    /// read-only display and full interactive editing. Implements
    /// <see cref="ISchematicHost"/> so the head-agnostic editor tools in
    /// <c>SchematicControls.Editor</c> drive it without knowing about Avalonia.
    /// </summary>
    public class SchematicCanvas : Control, ISchematicHost
    {
        private const int Grid = 5;
        private const int MajorGrid = 100;
        private const int MinorGrid = 10;
        private const double MarginUnits = 20;

        private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        private static readonly IPen MinorGridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xFF)), 0.1);
        private static readonly IPen MajorGridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0xFF)), 0.1);
        private static readonly IPen SelectionRectPen = new Pen(Brushes.Blue, 1.0)
        {
            DashStyle = new global::Avalonia.Media.DashStyle(new double[] { 2, 1 }, 0)
        };
        private static readonly IPen WirePreviewPen = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.0)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        private static readonly EdgeStyle GhostEdge =
            new EdgeStyle(DrawColor.Gray, 1.0);
        private static readonly TextStyle DefaultTextStyle =
            new TextStyle("Courier New", 10.0, SchematicControls.Abstractions.FontWeight.Normal, DrawColor.Black);

        private readonly Dictionary<Circuit.Element, ElementVisual> visuals =
            new Dictionary<Circuit.Element, ElementVisual>();
        private readonly AvaloniaDrawingContextFactory factory = new AvaloniaDrawingContextFactory();

        private Circuit.Schematic schematic;
        private readonly EditStack edits = new EditStack();
        private IEditorTool tool;

        private DrawPoint origin;
        private Circuit.Coord? selRectA;
        private Circuit.Coord? selRectB;
        private SymbolVisual ghostVisual;
        private IReadOnlyList<Circuit.Coord> wirePreview;
        private Circuit.Coord? lastMouse;

        static SchematicCanvas()
        {
            FocusableProperty.OverrideDefaultValue<SchematicCanvas>(true);
        }

        public SchematicCanvas()
        {
            ClipToBounds = false;
            Focusable = true;
            Tool = new SelectionTool(this);
        }

        /// <summary>Fires after any selection state change. Listeners that read the host's
        /// <c>Selected</c> enumeration will see the post-change set.</summary>
        public event EventHandler SelectionChanged;

        private void RaiseSelectionChanged()
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- Schematic binding ----

        public Circuit.Schematic Schematic
        {
            get => schematic;
            set
            {
                if (schematic == value) return;
                UnsubscribeSchematic();
                schematic = value;
                SubscribeSchematic();
                BuildVisuals();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public EditStack Edits => edits;

        public IReadOnlyDictionary<Circuit.Element, ElementVisual> Visuals => visuals;

        private void SubscribeSchematic()
        {
            if (schematic == null) return;
            schematic.Elements.ItemAdded += OnElementAdded;
            schematic.Elements.ItemRemoved += OnElementRemoved;
        }

        private void UnsubscribeSchematic()
        {
            if (schematic == null) return;
            schematic.Elements.ItemAdded -= OnElementAdded;
            schematic.Elements.ItemRemoved -= OnElementRemoved;
            foreach (ElementVisual v in visuals.Values)
                v.VisualChanged -= OnVisualChanged;
            visuals.Clear();
        }

        private void BuildVisuals()
        {
            if (schematic == null) return;
            foreach (Circuit.Element e in schematic.Elements)
                AddVisual(e);
        }

        private void AddVisual(Circuit.Element e)
        {
            ElementVisual v = ElementVisual.For(e);
            v.VisualChanged += OnVisualChanged;
            visuals[e] = v;
        }

        private void OnElementAdded(object sender, Circuit.ElementEventArgs e)
        {
            AddVisual(e.Element);
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void OnElementRemoved(object sender, Circuit.ElementEventArgs e)
        {
            if (visuals.TryGetValue(e.Element, out ElementVisual v))
            {
                v.VisualChanged -= OnVisualChanged;
                visuals.Remove(e.Element);
            }
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void OnVisualChanged(object sender, EventArgs e) => InvalidateVisual();

        // ---- Layout / Rendering ----

        protected override Size MeasureOverride(Size availableSize)
        {
            if (schematic == null || !HasAnyElements())
                return new Size(MajorGrid, MajorGrid);
            Circuit.Coord lb = schematic.LowerBound;
            Circuit.Coord ub = schematic.UpperBound;
            double w = ub.x - lb.x + 2 * MarginUnits;
            double h = ub.y - lb.y + 2 * MarginUnits;
            return new Size(Math.Max(w, MajorGrid), Math.Max(h, MajorGrid));
        }

        private bool HasAnyElements()
        {
            foreach (Circuit.Element _ in schematic.Elements) return true;
            return false;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect bounds = new Rect(Bounds.Size);
            context.FillRectangle(BackgroundBrush, bounds);

            origin = new DrawPoint(MarginUnits, MarginUnits);
            if (schematic != null && HasAnyElements())
            {
                Circuit.Coord lb = schematic.LowerBound;
                origin = new DrawPoint(MarginUnits - lb.x, MarginUnits - lb.y);
            }

            using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                DrawGrid(context, bounds);
                if (schematic != null)
                    DrawElements(context);
                DrawOverlays(context);
            }
        }

        private void DrawGrid(DrawingContext context, Rect ctlBounds)
        {
            double worldX1 = -origin.X;
            double worldY1 = -origin.Y;
            double worldX2 = worldX1 + ctlBounds.Width;
            double worldY2 = worldY1 + ctlBounds.Height;

            int startX = (int)Math.Floor(worldX1 / MinorGrid) * MinorGrid;
            int startY = (int)Math.Floor(worldY1 / MinorGrid) * MinorGrid;
            int endX = (int)Math.Ceiling(worldX2 / MinorGrid) * MinorGrid;
            int endY = (int)Math.Ceiling(worldY2 / MinorGrid) * MinorGrid;

            for (int x = startX; x <= endX; x += MinorGrid)
            {
                IPen pen = (x % MajorGrid == 0) ? MajorGridPen : MinorGridPen;
                context.DrawLine(pen, new Point(x, worldY1), new Point(x, worldY2));
            }
            for (int y = startY; y <= endY; y += MinorGrid)
            {
                IPen pen = (y % MajorGrid == 0) ? MajorGridPen : MinorGridPen;
                context.DrawLine(pen, new Point(worldX1, y), new Point(worldX2, y));
            }
        }

        private void DrawElements(DrawingContext context)
        {
            AvaloniaDrawingContext adapter = new AvaloniaDrawingContext(context);
            foreach (Circuit.Element e in schematic.Elements)
                if (e is Circuit.Wire && visuals.TryGetValue(e, out ElementVisual w))
                    w.Render(adapter, factory, DefaultTextStyle);
            foreach (Circuit.Element e in schematic.Elements)
                if (e is Circuit.Symbol && visuals.TryGetValue(e, out ElementVisual s))
                    s.Render(adapter, factory, DefaultTextStyle);
        }

        private void DrawOverlays(DrawingContext context)
        {
            // Ghost symbol (SymbolTool placement preview)
            if (ghostVisual != null)
            {
                AvaloniaDrawingContext adapter = new AvaloniaDrawingContext(context);
                ghostVisual.Render(adapter, factory, null);
            }

            // Wire-path preview (WireTool)
            if (wirePreview != null && wirePreview.Count >= 2)
            {
                for (int i = 0; i < wirePreview.Count - 1; i++)
                {
                    Circuit.Coord a = wirePreview[i];
                    Circuit.Coord b = wirePreview[i + 1];
                    context.DrawLine(WirePreviewPen, new Point(a.x, a.y), new Point(b.x, b.y));
                }
            }

            // Rubber-band selection rectangle
            if (selRectA.HasValue && selRectB.HasValue)
            {
                Circuit.Coord a = selRectA.Value, b = selRectB.Value;
                double x1 = Math.Min(a.x, b.x), x2 = Math.Max(a.x, b.x);
                double y1 = Math.Min(a.y, b.y), y2 = Math.Max(a.y, b.y);
                context.DrawRectangle(null, SelectionRectPen, new Rect(x1, y1, x2 - x1, y2 - y1));
            }
        }

        // ---- Input ----

        public IEditorTool Tool
        {
            get => tool;
            set
            {
                if (tool == value) return;
                tool?.End();
                tool = value;
                tool?.Begin();
                if (lastMouse.HasValue && tool != null)
                {
                    PointerEventArgs synth = new PointerEventArgs(lastMouse.Value, MouseButtons.None, KeyModifiers.None);
                    tool.MouseEnter(synth);
                    tool.MouseMove(synth);
                }
            }
        }

        private Circuit.Coord SnapLocalToGrid(Point local)
        {
            int wx = (int)Math.Round((local.X - origin.X) / Grid) * Grid;
            int wy = (int)Math.Round((local.Y - origin.Y) / Grid) * Grid;
            return new Circuit.Coord(wx, wy);
        }

        public Circuit.Coord SnapToGrid(Circuit.Point p)
        {
            int wx = (int)Math.Round(p.x / (double)Grid) * Grid;
            int wy = (int)Math.Round(p.y / (double)Grid) * Grid;
            return new Circuit.Coord(wx, wy);
        }

        private static KeyModifiers ToModifiers(AvKeyModifiers k)
        {
            KeyModifiers r = KeyModifiers.None;
            if ((k & AvKeyModifiers.Control) != 0) r |= KeyModifiers.Control;
            if ((k & AvKeyModifiers.Shift) != 0) r |= KeyModifiers.Shift;
            if ((k & AvKeyModifiers.Alt) != 0) r |= KeyModifiers.Alt;
            if ((k & AvKeyModifiers.Meta) != 0) r |= KeyModifiers.Windows;
            return r;
        }

        private static MouseButtons ToButtons(PointerPointProperties p)
        {
            MouseButtons b = MouseButtons.None;
            if (p.IsLeftButtonPressed) b |= MouseButtons.Left;
            if (p.IsRightButtonPressed) b |= MouseButtons.Right;
            if (p.IsMiddleButtonPressed) b |= MouseButtons.Middle;
            return b;
        }

        private PointerEventArgs MakeArgs(AvPointerEventArgs e, int clicks = 1)
        {
            Point local = e.GetPosition(this);
            Circuit.Coord at = SnapLocalToGrid(local);
            lastMouse = at;
            return new PointerEventArgs(at, ToButtons(e.GetCurrentPoint(this).Properties), ToModifiers(e.KeyModifiers), clicks);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            PointerPoint pt = e.GetCurrentPoint(this);
            int clicks = e.ClickCount;
            PointerEventArgs args = MakeArgs(e, clicks);

            if (pt.Properties.IsLeftButtonPressed)
            {
                if (clicks >= 2) tool?.MouseDoubleClick(args);
                else tool?.MouseDown(args);
                e.Pointer.Capture(this);
                e.Handled = true;
            }
            else if (pt.Properties.IsRightButtonPressed)
            {
                tool?.Cancel();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            PointerEventArgs args = MakeArgs(e);
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                tool?.MouseUp(args);
                if (e.Pointer.Captured == this) e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(AvPointerEventArgs e)
        {
            PointerEventArgs args = MakeArgs(e);
            tool?.MouseMove(args);
            e.Handled = true;
        }

        protected override void OnPointerEntered(AvPointerEventArgs e)
        {
            tool?.MouseEnter(MakeArgs(e));
        }

        protected override void OnPointerExited(AvPointerEventArgs e)
        {
            tool?.MouseLeave(MakeArgs(e));
            lastMouse = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            KeyCode k = MapKey(e.Key);
            if (tool != null && tool.KeyDown(new KeyboardEventArgs(k, ToModifiers(e.KeyModifiers))))
                e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            KeyCode k = MapKey(e.Key);
            if (tool != null && tool.KeyUp(new KeyboardEventArgs(k, ToModifiers(e.KeyModifiers))))
                e.Handled = true;
        }

        private static KeyCode MapKey(Key k) => k switch
        {
            Key.Escape => KeyCode.Escape,
            Key.Left => KeyCode.Left,
            Key.Right => KeyCode.Right,
            Key.Up => KeyCode.Up,
            Key.Down => KeyCode.Down,
            Key.Space => KeyCode.Space,
            Key.Enter => KeyCode.Enter,
            Key.Delete => KeyCode.Delete,
            Key.Tab => KeyCode.Tab,
            Key.LeftShift or Key.RightShift => KeyCode.Shift,
            Key.LeftCtrl or Key.RightCtrl => KeyCode.Control,
            Key.LeftAlt or Key.RightAlt => KeyCode.Alt,
            _ => KeyCode.None,
        };

        // ---- ISchematicHost ----

        IEnumerable<Circuit.Element> ISchematicHost.Elements => schematic?.Elements ?? Enumerable.Empty<Circuit.Element>();

        IEnumerable<Circuit.Element> ISchematicHost.Selected
        {
            get
            {
                foreach (var pair in visuals)
                    if (pair.Value.Selected) yield return pair.Key;
            }
        }

        IEnumerable<Circuit.Element> ISchematicHost.Symbols =>
            schematic?.Elements.OfType<Circuit.Symbol>().Cast<Circuit.Element>() ?? Enumerable.Empty<Circuit.Element>();

        IEnumerable<Circuit.Wire> ISchematicHost.Wires =>
            schematic?.Elements.OfType<Circuit.Wire>() ?? Enumerable.Empty<Circuit.Wire>();

        IEnumerable<Circuit.Element> ISchematicHost.AtPoint(Circuit.Coord at) =>
            ((ISchematicHost)this).InRect(at - 1, at + 1);

        IEnumerable<Circuit.Element> ISchematicHost.InRect(Circuit.Coord a, Circuit.Coord b)
        {
            if (schematic == null) yield break;
            Circuit.Coord lb = new Circuit.Coord(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
            Circuit.Coord ub = new Circuit.Coord(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
            foreach (Circuit.Element e in schematic.Elements)
                if (e.Intersects(lb, ub)) yield return e;
        }

        void ISchematicHost.Select(IEnumerable<Circuit.Element> selection, bool replace, bool toggle)
        {
            HashSet<Circuit.Element> set = new HashSet<Circuit.Element>(selection);
            bool changed = false;
            foreach (var pair in visuals)
            {
                bool inSet = set.Contains(pair.Key);
                if (inSet)
                {
                    if (toggle) { pair.Value.Selected = !pair.Value.Selected; changed = true; }
                    else if (!pair.Value.Selected) { pair.Value.Selected = true; changed = true; }
                }
                else if (replace)
                {
                    if (pair.Value.Selected) { pair.Value.Selected = false; changed = true; }
                }
            }
            if (changed) { InvalidateVisual(); RaiseSelectionChanged(); }
        }

        void ISchematicHost.ToggleSelect(Circuit.Element element)
        {
            if (element == null) return;
            if (visuals.TryGetValue(element, out ElementVisual v))
            {
                v.Selected = !v.Selected;
                InvalidateVisual();
                RaiseSelectionChanged();
            }
        }

        void ISchematicHost.Highlight(IEnumerable<Circuit.Element> elements)
        {
            HashSet<Circuit.Element> set = new HashSet<Circuit.Element>(elements);
            foreach (var pair in visuals)
                pair.Value.Highlighted = set.Contains(pair.Key);
        }

        void ISchematicHost.Add(IEnumerable<Circuit.Element> elements)
        {
            if (schematic == null) return;
            List<Circuit.Element> list = elements.ToList();
            if (list.Count == 0) return;
            edits.Do(new AddElements(schematic, list));
        }

        void ISchematicHost.Remove(IEnumerable<Circuit.Element> elements)
        {
            if (schematic == null) return;
            List<Circuit.Element> list = elements.ToList();
            if (list.Count == 0) return;
            edits.Do(new RemoveElements(schematic, list));
        }

        void ISchematicHost.BeginEditGroup() => edits.BeginEditGroup();
        void ISchematicHost.EndEditGroup() => edits.EndEditGroup();

        void ISchematicHost.SetCursor(SchematicCursor c)
        {
            Cursor = c switch
            {
                SchematicCursor.Cross => new AvCursor(StandardCursorType.Cross),
                SchematicCursor.SizeAll => new AvCursor(StandardCursorType.SizeAll),
                SchematicCursor.Hand => new AvCursor(StandardCursorType.Hand),
                SchematicCursor.Pen => new AvCursor(StandardCursorType.Cross),
                SchematicCursor.None => new AvCursor(StandardCursorType.None),
                _ => new AvCursor(StandardCursorType.Arrow),
            };
        }

        void ISchematicHost.SetSelectionRect(Circuit.Coord? a, Circuit.Coord? b)
        {
            selRectA = a; selRectB = b;
            InvalidateVisual();
        }

        void ISchematicHost.SetGhostSymbol(Circuit.Symbol symbol)
        {
            ghostVisual = symbol == null ? null : new SymbolVisual(symbol) { ShowText = false, ShowTerminals = true, Highlighted = true };
            InvalidateVisual();
        }

        void ISchematicHost.SetWirePathPreview(IReadOnlyList<Circuit.Coord> path)
        {
            wirePreview = path;
            InvalidateVisual();
        }

        void ISchematicHost.AddWire(Circuit.Coord a, Circuit.Coord b) => WireRouting.AddWire(this, a, b);
        void ISchematicHost.AddWire(IList<Circuit.Coord> points) => WireRouting.AddWire(this, points);
        List<Circuit.Coord> ISchematicHost.FindWirePath(List<Circuit.Coord> trail) => WireRouting.FindWirePath(trail);

        // ---- Direct API for the host application ----

        public IEnumerable<Circuit.Element> SelectedElements => ((ISchematicHost)this).Selected;

        public void DeleteSelection()
        {
            List<Circuit.Element> sel = SelectedElements.ToList();
            if (sel.Count == 0) return;
            edits.Do(new RemoveElements(schematic, sel));
        }

        public void SelectAll() => ((ISchematicHost)this).Select(schematic?.Elements ?? Enumerable.Empty<Circuit.Element>(), true, false);
    }
}
