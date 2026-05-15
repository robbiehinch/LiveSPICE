using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace SchematicControls.Abstractions
{
    /// <summary>
    /// Head-agnostic per-element renderer for schematic elements. Holds selection/highlight
    /// state and knows how to render the element through an <see cref="IDrawingContext"/> in
    /// world coordinates. The WPF head's <c>ElementControl</c> hierarchy and the Avalonia
    /// head's <c>SchematicCanvas</c> both ultimately drive their drawing through these.
    /// </summary>
    public abstract class ElementVisual
    {
        protected readonly Circuit.Element element;
        public Circuit.Element Element => element;

        private bool selected;
        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value) return;
                selected = value;
                OnVisualChanged();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool highlighted;
        public bool Highlighted
        {
            get => highlighted;
            set
            {
                if (highlighted == value) return;
                highlighted = value;
                OnVisualChanged();
            }
        }

        private bool showTerminals = true;
        public bool ShowTerminals
        {
            get => showTerminals;
            set { if (showTerminals == value) return; showTerminals = value; OnVisualChanged(); }
        }

        public event EventHandler VisualChanged;
        public event EventHandler SelectedChanged;

        protected ElementVisual(Circuit.Element e)
        {
            element = e;
            foreach (Circuit.Terminal t in e.Terminals)
                t.ConnectionChanged += (_, _) => OnVisualChanged();
        }

        protected void OnVisualChanged() => VisualChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Render the element in world coordinates. <paramref name="factory"/> may be null,
        /// in which case text is skipped (used by symbol-icon previews where no text is wanted).
        /// </summary>
        public abstract void Render(IDrawingContext context, IDrawingContextFactory factory, TextStyle? textStyle);

        public static ElementVisual For(Circuit.Element e) => e switch
        {
            Circuit.Wire w => new WireVisual(w),
            Circuit.Symbol s => new SymbolVisual(s),
            _ => throw new NotSupportedException("Unknown element type: " + e.GetType()),
        };
    }

    /// <summary>
    /// Renderer for a <see cref="Circuit.Wire"/>.
    /// </summary>
    public sealed class WireVisual : ElementVisual
    {
        private static readonly DrawColor SelectedColor = DrawColor.DodgerBlue;
        private static readonly DrawColor HighlightedColor = DrawColor.Gray;

        public Circuit.Wire Wire => (Circuit.Wire)element;

        public WireVisual(Circuit.Wire w) : base(w)
        {
            w.LayoutChanged += (_, _) => OnVisualChanged();
        }

        public override void Render(IDrawingContext context, IDrawingContextFactory factory, TextStyle? textStyle)
        {
            DrawColor color = Selected ? SelectedColor : (Highlighted ? HighlightedColor : EdgePalette.WireColor);
            EdgeStyle line = new EdgeStyle(color, EdgePalette.EdgeThickness);
            EdgeStyle terminal = new EdgeStyle(EdgePalette.WireColor, EdgePalette.EdgeThickness);

            DrawPoint a = new DrawPoint(Wire.A.x, Wire.A.y);
            DrawPoint b = new DrawPoint(Wire.B.x, Wire.B.y);
            context.DrawLine(line, a, b);

            if (ShowTerminals)
            {
                DrawPoint dx = new DrawPoint(EdgePalette.WireTerminalSize / 2, EdgePalette.WireTerminalSize / 2);
                context.DrawRectangle(EdgePalette.WireColor, terminal, new DrawRect(a - dx, a + dx));
                context.DrawRectangle(EdgePalette.WireColor, terminal, new DrawRect(b - dx, b + dx));
            }
        }
    }

    /// <summary>
    /// Renderer for a <see cref="Circuit.Symbol"/>. Listens to component property changes and
    /// invalidates so the host can re-render.
    /// </summary>
    public sealed class SymbolVisual : ElementVisual
    {
        private static readonly EdgeStyle SelectedEdge =
            new EdgeStyle(DrawColor.DodgerBlue, 1.0, DashStyle.Dashed);
        private static readonly EdgeStyle HighlightedEdge =
            new EdgeStyle(DrawColor.Gray, 1.0, DashStyle.Dashed);

        private Circuit.SymbolLayout layout;
        private bool showText = true;

        public Circuit.Symbol Symbol => (Circuit.Symbol)element;
        public Circuit.Component Component => Symbol.Component;

        public bool ShowText
        {
            get => showText;
            set { if (showText == value) return; showText = value; OnVisualChanged(); }
        }

        public SymbolVisual(Circuit.Symbol s) : base(s)
        {
            layout = s.Component.LayoutSymbol();
            s.Component.PropertyChanged += (_, _) => RefreshLayout();
            s.LayoutChanged += (_, _) => OnVisualChanged();
        }

        private void RefreshLayout()
        {
            layout = Component.LayoutSymbol();
            OnVisualChanged();
        }

        public override void Render(IDrawingContext context, IDrawingContextFactory factory, TextStyle? textStyle)
        {
            DrawMatrix transform = BuildWorldTransform();
            TextStyle? text = showText ? textStyle : null;
            SymbolLayoutRenderer.Draw(layout, context, factory, transform, null, text);

            if (Selected || Highlighted)
            {
                Circuit.Coord lb = Symbol.LowerBound;
                Circuit.Coord ub = Symbol.UpperBound;
                DrawRect bounds = new DrawRect(
                    new DrawPoint(lb.x, lb.y),
                    new DrawPoint(ub.x, ub.y));
                context.DrawRectangle(null, Selected ? SelectedEdge : HighlightedEdge, bounds);
            }
        }

        private DrawMatrix BuildWorldTransform()
        {
            Circuit.Coord off = (layout.LowerBound + layout.UpperBound) / 2;
            Circuit.Coord lb = Symbol.LowerBound;
            Circuit.Coord ub = Symbol.UpperBound;
            double wcx = (lb.x + ub.x) / 2.0;
            double wcy = (lb.y + ub.y) / 2.0;

            int sy = Symbol.Flip ? 1 : -1;
            double theta = Symbol.Rotation * -Math.PI / 2.0;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            if (Math.Abs(c) < 1e-12) c = 0;
            if (Math.Abs(s) < 1e-12) s = 0;

            return new DrawMatrix
            {
                M11 = c,
                M12 = s,
                M21 = -s * sy,
                M22 = c * sy,
                OffsetX = -off.x * c + off.y * sy * s + wcx,
                OffsetY = -off.x * s - off.y * sy * c + wcy,
            };
        }

        /// <summary>
        /// Build a tooltip string for the symbol at a given world-coord pointer position.
        /// If the pointer is within <paramref name="terminalTolerance"/> world-units of a
        /// terminal, returns the terminal label; otherwise a multi-line component summary.
        /// Returns null when there's nothing to show.
        /// </summary>
        public string TooltipAt(DrawPoint pointerWorld, double terminalTolerance)
        {
            DrawMatrix t = BuildWorldTransform();
            foreach (Circuit.Terminal term in Symbol.Terminals)
            {
                Circuit.Coord tx = layout.MapTerminal(term);
                DrawPoint tp = t.Transform(new DrawPoint(tx.x, tx.y));
                DrawPoint d = tp - pointerWorld;
                if (Math.Sqrt(d.X * d.X + d.Y * d.Y) < terminalTolerance)
                    return "Terminal '" + term.ToString() + "'";
            }

            Circuit.Component c = Symbol.Component;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(c.ToString());
            foreach (PropertyInfo p in c.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(j => j.GetCustomAttribute<Circuit.Serialize>() != null &&
                            (j.GetCustomAttribute<BrowsableAttribute>() == null ||
                             j.GetCustomAttribute<BrowsableAttribute>().Browsable)))
            {
                object value = p.GetValue(c, null);
                DefaultValueAttribute def = p.GetCustomAttribute<DefaultValueAttribute>();
                if (def == null || !Equals(def.Value, value))
                {
                    TypeConverter tc = TypeDescriptor.GetConverter(p.PropertyType);
                    sb.Append('\n').Append(p.Name).Append(" = ").Append(tc.ConvertToString(value));
                }
            }
            return sb.ToString();
        }
    }
}
