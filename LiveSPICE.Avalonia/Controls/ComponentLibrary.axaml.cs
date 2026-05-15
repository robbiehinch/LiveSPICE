using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveSPICE.Avalonia.Services;

namespace LiveSPICE.Avalonia.Controls
{
    public partial class ComponentLibrary : UserControl
    {
        private CatalogCategory root;
        private List<ComponentEntry> flat;

        public event Action<Circuit.Component> ComponentClick;

        /// <summary>All catalog entries (flat). Lazily-populated; empty until the panel loads.</summary>
        public IEnumerable<ComponentEntry> AllEntries => flat ?? Enumerable.Empty<ComponentEntry>();

        public ComponentLibrary()
        {
            InitializeComponent();
            Loaded += (_, _) => InitialiseIfNeeded();
            filterBox.TextChanged += (_, _) => ApplyFilter(filterBox.Text);
        }

        private void InitialiseIfNeeded()
        {
            if (root != null) return;
            root = ComponentCatalog.BuildRoot();
            flat = root.Children.SelectMany(c => c.Flatten).ToList();
            tree.ItemsSource = root.Children;
            filtered.ItemsSource = flat;
        }

        private void ApplyFilter(string text)
        {
            string f = (text ?? string.Empty).Trim().ToUpperInvariant();
            if (f.Length == 0)
            {
                tree.IsVisible = true;
                filtered.IsVisible = false;
                if (flat != null)
                    foreach (ComponentEntry e in flat) e.IsVisible = true;
                return;
            }
            tree.IsVisible = false;
            filtered.IsVisible = true;
            if (flat == null) return;
            foreach (ComponentEntry e in flat)
                e.IsVisible = e.Name?.ToUpperInvariant().Contains(f) == true;
        }

        private void ClearFilterClicked(object sender, RoutedEventArgs e) => filterBox.Text = string.Empty;

        private void ComponentClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Control ctl && ctl.Tag is ComponentEntry entry)
                ComponentClick?.Invoke(entry.Instance);
        }
    }
}
