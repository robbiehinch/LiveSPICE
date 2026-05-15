using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiveSPICE.Avalonia.Controls;
using LiveSPICE.Avalonia.Services;
using SchematicControls.Editor;
using SchematicControls.Editor.Tools;
using Util;

namespace LiveSPICE.Avalonia
{
    public partial class MainWindow : Window
    {
        private Settings settings;
        private SchematicEditorCore core;
        private SchematicCanvas canvas;

        public MainWindow()
        {
            InitializeComponent();
            settings = Settings.Load();
            canvas = schematicView.Canvas;
            core = new SchematicEditorCore(canvas);
            core.FilePathChanged += (_, _) => UpdateTitle();
            core.DirtyChanged += (_, _) => UpdateTitle();
            canvas.SelectionChanged += (_, _) => UpdateProperties();
            library.ComponentClick += OnLibraryComponentClick;

            NewSchematic();
            RebuildRecentMenu();
            UpdateTitle();
            UpdateToolStatus();
            UpdateProperties();
        }

        private void UpdateTitle()
        {
            string name = core.FilePath != null ? Path.GetFileName(core.FilePath) : "Untitled";
            Title = "LiveSPICE — " + (core.Dirty ? "*" : "") + name;
        }

        private void UpdateToolStatus()
        {
            toolStatus.Text = "Tool: " + (canvas.Tool?.GetType().Name ?? "(none)");
        }

        private void UpdateProperties()
        {
            Circuit.Element first = canvas.SelectedElements.FirstOrDefault();
            if (first == null)
            {
                propertiesText.Text = "(no selection)";
                return;
            }
            propertiesText.Text = DescribeElement(first);
        }

        private static string DescribeElement(Circuit.Element e)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(e.GetType().Name);
            sb.AppendLine(e.ToString());
            if (e is Circuit.Symbol sym)
            {
                Circuit.Component c = sym.Component;
                sb.AppendLine();
                sb.AppendLine("Component: " + c.GetType().Name);
                foreach (PropertyInfo p in c.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(j => j.GetCustomAttribute<Circuit.Serialize>() != null &&
                                (j.GetCustomAttribute<BrowsableAttribute>() == null ||
                                 j.GetCustomAttribute<BrowsableAttribute>().Browsable)))
                {
                    object v;
                    try { v = p.GetValue(c, null); } catch { continue; }
                    sb.Append(p.Name).Append(" = ").AppendLine(v?.ToString() ?? "(null)");
                }
            }
            return sb.ToString();
        }

        private void OnLibraryComponentClick(Circuit.Component proto)
        {
            canvas.Tool = new SymbolTool(canvas, proto);
            UpdateToolStatus();
            canvas.Focus();
        }

        // ---------- Schematic lifecycle ----------

        private void NewSchematic()
        {
            canvas.Schematic = new Circuit.Schematic(new NullLog());
            core.SetFilePath(null);
            core.MarkClean();
            canvas.Tool = new SelectionTool(canvas);
            UpdateToolStatus();
            UpdateProperties();
        }

        public bool TryLoadSchematic(string path)
        {
            if (!File.Exists(path))
            {
                statusText.Text = "File not found: " + path;
                settings.RemoveRecent(path);
                RebuildRecentMenu();
                return false;
            }
            try
            {
                Circuit.Schematic s = Circuit.Schematic.Load(path, new NullLog());
                canvas.Schematic = s;
                core.SetFilePath(path);
                core.MarkClean();
                canvas.Tool = new SelectionTool(canvas);
                UpdateToolStatus();
                UpdateProperties();
                statusText.Text = path;
                settings.NoteRecent(path);
                RebuildRecentMenu();
                return true;
            }
            catch (Exception ex)
            {
                statusText.Text = "Failed to load: " + ex.Message;
                return false;
            }
        }

        private async Task OpenWithDialogAsync()
        {
            FilePickerOpenOptions opts = new FilePickerOpenOptions
            {
                Title = "Open Schematic",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Circuit Schematics") { Patterns = new[] { "*.schx" } },
                    new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } },
                },
            };
            IReadOnlyList<IStorageFile> picks = await StorageProvider.OpenFilePickerAsync(opts);
            IStorageFile file = picks.FirstOrDefault();
            if (file != null && file.TryGetLocalPath() is string local)
                TryLoadSchematic(local);
        }

        private async Task<bool> SaveAsWithDialogAsync()
        {
            FilePickerSaveOptions opts = new FilePickerSaveOptions
            {
                Title = "Save Schematic As",
                DefaultExtension = "schx",
                ShowOverwritePrompt = true,
                SuggestedFileName = core.FilePath != null ? Path.GetFileName(core.FilePath) : "Untitled.schx",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Circuit Schematics") { Patterns = new[] { "*.schx" } },
                    new FilePickerFileType("XML Files") { Patterns = new[] { "*.xml" } },
                },
            };
            IStorageFile file = await StorageProvider.SaveFilePickerAsync(opts);
            if (file == null) return false;
            string path = file.TryGetLocalPath();
            if (path == null) { statusText.Text = "Cannot save to non-local target."; return false; }
            bool ok = core.SaveTo(path);
            if (ok)
            {
                statusText.Text = "Saved: " + path;
                settings.NoteRecent(path);
                RebuildRecentMenu();
            }
            else statusText.Text = "Save failed.";
            return ok;
        }

        private async Task<bool> SaveAsync()
        {
            if (core.FilePath == null) return await SaveAsWithDialogAsync();
            bool ok = core.Save();
            statusText.Text = ok ? "Saved: " + core.FilePath : "Save failed.";
            if (ok) settings.NoteRecent(core.FilePath);
            return ok;
        }

        // ---------- Menu handlers ----------

        private void NewClicked(object sender, RoutedEventArgs e) => NewSchematic();
        private async void OpenClicked(object sender, RoutedEventArgs e) => await OpenWithDialogAsync();
        private async void SaveClicked(object sender, RoutedEventArgs e) => await SaveAsync();
        private async void SaveAsClicked(object sender, RoutedEventArgs e) => await SaveAsWithDialogAsync();
        private void ExitClicked(object sender, RoutedEventArgs e) => Close();

        private void UndoClicked(object sender, RoutedEventArgs e)
        {
            canvas.Edits.Undo();
            UpdateTitle();
            UpdateProperties();
            canvas.InvalidateVisual();
        }
        private void RedoClicked(object sender, RoutedEventArgs e)
        {
            canvas.Edits.Redo();
            UpdateTitle();
            UpdateProperties();
            canvas.InvalidateVisual();
        }
        private void DeleteClicked(object sender, RoutedEventArgs e)
        {
            canvas.DeleteSelection();
            UpdateTitle();
            UpdateProperties();
        }
        private void SelectAllClicked(object sender, RoutedEventArgs e) { canvas.SelectAll(); UpdateProperties(); }

        private void ToolSelectionClicked(object sender, RoutedEventArgs e)
        {
            canvas.Tool = new SelectionTool(canvas);
            UpdateToolStatus();
        }
        private void ToolWireClicked(object sender, RoutedEventArgs e)
        {
            canvas.Tool = new WireTool(canvas);
            UpdateToolStatus();
        }

        private void AboutClicked(object sender, RoutedEventArgs e)
        {
            statusText.Text = "LiveSPICE Avalonia head — Phase 4 build.";
        }

        // ---------- MRU ----------

        private void RebuildRecentMenu()
        {
            recentMenu.Items.Clear();
            if (settings.Mru.Count == 0)
            {
                MenuItem empty = new MenuItem { Header = "(empty)", IsEnabled = false };
                recentMenu.Items.Add(empty);
                return;
            }
            foreach (string p in settings.Mru)
            {
                string captured = p;
                MenuItem mi = new MenuItem { Header = TruncateForMenu(captured) };
                mi.Click += (_, _) => TryLoadSchematic(captured);
                recentMenu.Items.Add(mi);
            }
        }

        private static string TruncateForMenu(string path)
        {
            const int Max = 60;
            return path.Length <= Max ? path : "..." + path.Substring(path.Length - Max + 3);
        }
    }
}
