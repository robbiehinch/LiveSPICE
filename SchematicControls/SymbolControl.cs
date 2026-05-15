using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SchematicControls.Wpf;
using Util;
using AbsColor = SchematicControls.Abstractions.DrawColor;
using AbsEdgeStyle = SchematicControls.Abstractions.EdgeStyle;
using AbsDashStyle = SchematicControls.Abstractions.DashStyle;
using AbsLineCap = SchematicControls.Abstractions.LineCap;
using AbsTextStyle = SchematicControls.Abstractions.TextStyle;
using AbsDrawMatrix = SchematicControls.Abstractions.DrawMatrix;
using AbsFontWeight = SchematicControls.Abstractions.FontWeight;
using AbsRenderer = SchematicControls.Abstractions.SymbolLayoutRenderer;

namespace SchematicControls
{
    /// <summary>
    /// Element for a symbol.
    /// </summary>
    public class SymbolControl : ElementControl
    {
        static SymbolControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SymbolControl), new FrameworkPropertyMetadata(typeof(SymbolControl)));
        }

        private bool showText = true;
        public bool ShowText { get { return showText; } set { showText = value; InvalidateVisual(); } }

        protected Circuit.SymbolLayout layout;

        public SymbolControl(Circuit.Symbol S) : base(S)
        {
            layout = Component.LayoutSymbol();

            S.Component.PropertyChanged += (o, e) => RefreshLayout();

            MouseMove += OnMouseMove;
        }
        public SymbolControl(Circuit.Component C) : this(new Circuit.Symbol(C)) { }

        public Circuit.Symbol Symbol { get { return (Circuit.Symbol)element; } }
        public Circuit.Component Component { get { return Symbol.Component; } }
        public Vector Size { get { return new Vector(Symbol.Size.x, Symbol.Size.y); } }

        protected void OnMouseMove(object sender, MouseEventArgs e)
        {
            Point x = e.GetPosition(this);

            Matrix transform = Transform;

            foreach (Circuit.Terminal i in Symbol.Terminals)
            {
                Circuit.Coord tx = layout.MapTerminal(i);
                Point tp = new Point(tx.x, tx.y);
                tp = transform.Transform(tp);
                if ((tp - x).Length < 5.0)
                {
                    ToolTip = "Terminal '" + i.ToString() + "'";
                    return;
                }
            }

            TextBlock text = new TextBlock();

            Circuit.Component component = Symbol.Component;

            text.Inlines.Add(new Bold(new Run(component.ToString())));

            foreach (PropertyInfo i in component.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(j =>
                j.CustomAttribute<Circuit.Serialize>() != null &&
                (j.CustomAttribute<BrowsableAttribute>() == null || j.CustomAttribute<BrowsableAttribute>().Browsable)))
            {
                object value = i.GetValue(component, null);
                DefaultValueAttribute def = i.CustomAttribute<DefaultValueAttribute>();
                if (def == null || !Equals(def.Value, value))
                {
                    System.ComponentModel.TypeConverter tc = System.ComponentModel.TypeDescriptor.GetConverter(i.PropertyType);
                    text.Inlines.Add(new Run("\n" + i.Name + " = "));
                    text.Inlines.Add(new Bold(new Run(tc.ConvertToString(value))));
                }
            }

            ToolTip = new ToolTip() { Content = text };
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            Circuit.Symbol sym = Symbol;
            Point b1 = ToPoint(sym.LowerBound - sym.Position);
            Point b2 = ToPoint(sym.UpperBound - sym.Position);
            return new Size(Math.Abs(b2.X - b1.X), Math.Abs(b2.Y - b1.Y));
        }

        protected Matrix Transform
        {
            get
            {
                var offset = (layout.LowerBound + layout.UpperBound) / 2;

                Matrix transform = new Matrix();
                transform.Translate(-offset.x, -offset.y);
                transform.Scale(1.0, Symbol.Flip ? 1.0 : -1.0);
                transform.Rotate(Symbol.Rotation * -90);
                transform.Translate(Symbol.Width / 2, Symbol.Height / 2);
                return transform;
            }
        }

        protected void RefreshLayout()
        {
            layout = Component.LayoutSymbol();
            InvalidateVisual();
        }

        protected DrawingContext dc;
        protected override void OnRender(DrawingContext dc)
        {
            Matrix transform = Transform;

            DrawLayout(
                layout, dc, transform, Pen, ShowText ? FontFamily : null, FontWeight, FontSize,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            Rect bounds = new Rect(T(transform, layout.LowerBound), T(transform, layout.UpperBound));
            if (Selected)
                dc.DrawRectangle(null, SelectedPen, bounds);
            else if (Highlighted)
                dc.DrawRectangle(null, HighlightPen, bounds);
        }

        private static Point T(Matrix Tx, Circuit.Point x) { return Tx.Transform(new Point(x.x, x.y)); }

        public static void DrawLayout(
            Circuit.SymbolLayout Layout, DrawingContext Context, Matrix Tx, Pen Pen, FontFamily FontFamily,
            FontWeight FontWeight, double FontSize, double PixelsPerDip)
        {
            WpfDrawingContext adapter = new WpfDrawingContext(Context);
            WpfDrawingContextFactory factory = new WpfDrawingContextFactory(PixelsPerDip);

            AbsEdgeStyle? overrideEdge = Pen == null ? (AbsEdgeStyle?)null : WpfPenToEdge(Pen);
            AbsTextStyle? textStyle = FontFamily == null
                ? (AbsTextStyle?)null
                : new AbsTextStyle(
                    FontFamily.Source,
                    FontSize,
                    FontWeight == FontWeights.Bold ? AbsFontWeight.Bold : AbsFontWeight.Normal,
                    AbsColor.Black);

            AbsDrawMatrix tx = WpfConversions.FromMatrix(Tx);
            AbsRenderer.Draw(Layout, adapter, factory, tx, overrideEdge, textStyle);
        }

        private static AbsEdgeStyle WpfPenToEdge(Pen pen)
        {
            AbsColor color = AbsColor.Black;
            if (pen.Brush is SolidColorBrush scb)
                color = AbsColor.FromArgb(scb.Color.A, scb.Color.R, scb.Color.G, scb.Color.B);
            AbsDashStyle dash = pen.DashStyle == DashStyles.Dash ? AbsDashStyle.Dashed : AbsDashStyle.Solid;
            AbsLineCap cap = pen.StartLineCap == PenLineCap.Round ? AbsLineCap.Round : AbsLineCap.Flat;
            return new AbsEdgeStyle(color, pen.Thickness, dash, cap);
        }

        public static void DrawLayout(
            Circuit.SymbolLayout Layout,
            DrawingContext Context, Matrix Tx,
            FontFamily FontFamily, FontWeight FontWeight, double FontSize, double PixelsPerDip)
        {
            DrawLayout(Layout, Context, Tx, null, FontFamily, FontWeight, FontSize, PixelsPerDip);
        }

        public static void DrawLayout(
            Circuit.SymbolLayout Layout,
            DrawingContext Context, Matrix Tx,
            FontFamily FontFamily, double PixelsPerDip)
        {
            DrawLayout(Layout, Context, Tx, null, FontFamily, FontWeights.Normal, 10.0, PixelsPerDip);
        }

        public static void DrawLayout(
            Circuit.SymbolLayout Layout,
            DrawingContext Context, Matrix Tx, double PixelsPerDip)
        {
            DrawLayout(Layout, Context, Tx, new FontFamily("Courier New"), PixelsPerDip);
        }
    }
}
