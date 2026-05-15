using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SchematicControls.Editor.Edits;

namespace LiveSPICE.Avalonia.Controls
{
    /// <summary>
    /// Reflective property editor: for every <see cref="Circuit.Serialize"/>-annotated and
    /// browsable property on the bound component, lays out a labelled TextBox. Commits go
    /// through the supplied <see cref="EditStack"/> so the change is undoable. This is a
    /// stand-in for a full PropertyGrid library — works for simple value types via
    /// <see cref="TypeConverter"/>.
    /// </summary>
    public class PropertyPane : Control
    {
        private readonly StackPanel root = new StackPanel { Spacing = 4 };
        private Circuit.Component bound;
        private EditStack edits;

        public PropertyPane()
        {
            // Avalonia Control hosts a single child via VisualChildren / LogicalChildren.
            VisualChildren.Add(root);
            LogicalChildren.Add(root);
        }

        protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
        {
            root.Measure(availableSize);
            return root.DesiredSize;
        }

        protected override global::Avalonia.Size ArrangeOverride(global::Avalonia.Size finalSize)
        {
            root.Arrange(new global::Avalonia.Rect(finalSize));
            return finalSize;
        }

        public void Clear()
        {
            bound = null;
            edits = null;
            root.Children.Clear();
        }

        public void Show(string message)
        {
            Clear();
            TextBlock t = new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(0xcd, 0xcd, 0xcd)) };
            root.Children.Add(t);
        }

        public void Bind(Circuit.Component component, EditStack stack)
        {
            bound = component;
            edits = stack;
            root.Children.Clear();
            if (component == null) { Show("(no selection)"); return; }

            TextBlock header = new TextBlock
            {
                Text = component.GetType().Name,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xcd, 0xcd, 0xcd)),
            };
            root.Children.Add(header);

            foreach (PropertyInfo p in EditableProperties(component))
            {
                BuildRow(component, p);
            }
        }

        private static IEnumerable<PropertyInfo> EditableProperties(Circuit.Component c)
        {
            return c.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<Circuit.Serialize>() != null
                    && (p.GetCustomAttribute<BrowsableAttribute>() == null
                        || p.GetCustomAttribute<BrowsableAttribute>().Browsable)
                    && p.CanWrite
                    && p.CanRead);
        }

        private void BuildRow(Circuit.Component target, PropertyInfo property)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(property.PropertyType);
            object initial;
            try { initial = property.GetValue(target, null); } catch { return; }

            TextBlock label = new TextBlock
            {
                Text = property.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9c, 0xdc, 0xfe)),
                Margin = new global::Avalonia.Thickness(0, 4, 0, 0),
            };
            TextBox editor = new TextBox
            {
                Text = converter.CanConvertTo(typeof(string)) ? converter.ConvertToString(initial) : initial?.ToString() ?? string.Empty,
            };

            editor.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    Commit(target, property, converter, editor);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    object current;
                    try { current = property.GetValue(target, null); } catch { current = null; }
                    editor.Text = converter.CanConvertTo(typeof(string)) ? converter.ConvertToString(current) : current?.ToString() ?? string.Empty;
                    e.Handled = true;
                }
            };
            editor.LostFocus += (s, e) => Commit(target, property, converter, editor);

            root.Children.Add(label);
            root.Children.Add(editor);
        }

        private void Commit(Circuit.Component target, PropertyInfo property, TypeConverter converter, TextBox editor)
        {
            string text = editor.Text ?? string.Empty;
            object newValue;
            try
            {
                if (converter.CanConvertFrom(typeof(string)))
                    newValue = converter.ConvertFromInvariantString(text);
                else if (property.PropertyType == typeof(string))
                    newValue = text;
                else
                    return;
            }
            catch
            {
                // Bad input: revert the text but don't commit.
                object current;
                try { current = property.GetValue(target, null); } catch { current = null; }
                editor.Text = converter.CanConvertTo(typeof(string)) ? converter.ConvertToString(current) : current?.ToString() ?? string.Empty;
                return;
            }

            object existing;
            try { existing = property.GetValue(target, null); } catch { existing = null; }
            if (Equals(existing, newValue)) return;

            if (edits != null)
                edits.Do(new PropertyEdit(target, property, newValue));
            else
                property.SetValue(target, newValue, null);
        }
    }
}
