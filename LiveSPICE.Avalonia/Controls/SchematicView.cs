using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace LiveSPICE.Avalonia.Controls
{
    /// <summary>
    /// Scrollable, zoomable container around a <see cref="SchematicCanvas"/>. Hosts the
    /// canvas inside a <see cref="ScrollViewer"/> and provides Ctrl+wheel zoom (1.0 = world
    /// coords map 1:1 to pixels). Single-purpose for phase 2 (read-only view) — interactive
    /// editing comes in phase 3.
    /// </summary>
    public class SchematicView : TemplatedControl
    {
        public static readonly StyledProperty<double> ZoomProperty =
            AvaloniaProperty.Register<SchematicView, double>(nameof(Zoom), 2.0);

        public static readonly DirectProperty<SchematicView, SchematicCanvas> CanvasProperty =
            AvaloniaProperty.RegisterDirect<SchematicView, SchematicCanvas>(
                nameof(Canvas), o => o.Canvas);

        private readonly SchematicCanvas canvas;
        private readonly ScrollViewer scrollViewer;
        private readonly LayoutTransformControl transformHost;

        public SchematicView()
        {
            canvas = new SchematicCanvas();
            transformHost = new LayoutTransformControl
            {
                Child = canvas,
                LayoutTransform = new ScaleTransform(Zoom, Zoom),
            };
            scrollViewer = new ScrollViewer
            {
                Content = transformHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.LightGray,
            };

            VisualChildren.Add(scrollViewer);
            LogicalChildren.Add(scrollViewer);

            AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        }

        public SchematicCanvas Canvas => canvas;

        public double Zoom
        {
            get => GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ZoomProperty)
            {
                double z = (double)change.NewValue;
                transformHost.LayoutTransform = new ScaleTransform(z, z);
            }
        }

        private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                double next = Zoom * (e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2);
                Zoom = System.Math.Clamp(next, 0.25, 16.0);
                e.Handled = true;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            scrollViewer.Measure(availableSize);
            return scrollViewer.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            scrollViewer.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
