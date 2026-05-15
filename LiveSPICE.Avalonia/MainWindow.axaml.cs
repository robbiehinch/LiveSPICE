using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiveSPICE.Avalonia.Controls;
using LiveSPICE.Avalonia.Services;
using LiveSPICE.Avalonia.Windows;
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
        private LiveSimulationWindow activeSimulationWindow;

        /// <summary>The schematic canvas, exposed for the MCP host so tool handlers can
        /// mutate the schematic / push edits onto the same EditStack the UI uses.</summary>
        public SchematicCanvas Canvas => canvas;

        /// <summary>The currently-open live simulation window, or null if none. Set when
        /// the user (or an MCP call) opens the simulation; cleared when the window closes.</summary>
        public LiveSimulationWindow ActiveSimulationWindow => activeSimulationWindow;

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
            if (first is Circuit.Symbol sym)
            {
                propertyPane.Bind(sym.Component, canvas.Edits);
            }
            else if (first != null)
            {
                propertyPane.Show(first.GetType().Name + ": " + first.ToString());
            }
            else
            {
                propertyPane.Show("(no selection)");
            }
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

        private async void AboutClicked(object sender, RoutedEventArgs e)
        {
            AboutWindow w = new AboutWindow();
            await w.ShowDialog(this);
        }

        private async void ImportLtSpiceClicked(object sender, RoutedEventArgs e)
        {
            FilePickerOpenOptions opts = new FilePickerOpenOptions
            {
                Title = "Import LTSpice Schematic",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("LTSpice Schematics") { Patterns = new[] { "*.asc" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } },
                },
            };
            IReadOnlyList<IStorageFile> picks = await StorageProvider.OpenFilePickerAsync(opts);
            IStorageFile file = picks.FirstOrDefault();
            string path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                Circuit.LTSpiceImport.IPartLookup lookup = new CatalogPartLookup(library);
                var result = Circuit.LTSpiceImport.LTSpiceImporter.Import(path, lookup);
                Circuit.Schematic imported = result.Item1;
                Circuit.LTSpiceImport.ImportReport report = result.Item2;

                if (report.HasErrors)
                {
                    statusText.Text = "LTSpice import errors — see Log pane.";
                    AppendLog(FormatImportReport(report));
                    return;
                }

                canvas.Schematic = imported;
                core.SetFilePath(null);
                core.MarkClean();
                canvas.Tool = new SelectionTool(canvas);
                UpdateToolStatus();
                UpdateProperties();
                statusText.Text = "Imported from " + Path.GetFileName(path);
                AppendLog(report.Entries.Any() ? FormatImportReport(report) : "Imported " + path + " (no warnings).");
            }
            catch (Exception ex)
            {
                statusText.Text = "Import failed: " + ex.Message;
                AppendLog("Import failed: " + ex);
            }
        }

        private static string FormatImportReport(Circuit.LTSpiceImport.ImportReport report)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("LTSpice import:");
            foreach (var entry in report.Entries)
                sb.AppendLine("  " + entry);
            return sb.ToString();
        }

        private void AppendLog(string line)
        {
            logText.Text = string.IsNullOrEmpty(logText.Text) ? line : logText.Text + System.Environment.NewLine + line;
        }

        /// <summary>Bridges the Avalonia ComponentLibrary panel to the LTSpice importer's part lookup.</summary>
        private sealed class CatalogPartLookup : Circuit.LTSpiceImport.IPartLookup
        {
            private readonly ComponentLibrary library;
            public CatalogPartLookup(ComponentLibrary library) { this.library = library; }

            public Circuit.Component TryGetByPartNumber(string partNumber)
            {
                if (string.IsNullOrWhiteSpace(partNumber)) return null;
                foreach (Services.ComponentEntry entry in library.AllEntries)
                {
                    Circuit.Component c = entry.Instance;
                    if (c != null && !string.IsNullOrEmpty(c.PartNumber) &&
                        string.Equals(c.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
                return null;
            }
        }

        private void SimulateClicked(object sender, RoutedEventArgs e) => OpenSimulationFromMcp(startImmediately: false);

        /// <summary>Open the live simulation window (idempotent — focuses the existing one
        /// if already open). When <paramref name="startImmediately"/> is true, also triggers
        /// the Start action so the simulation begins without further user input.</summary>
        public void OpenSimulationFromMcp(bool startImmediately)
        {
            if (canvas.Schematic == null) { statusText.Text = "No schematic to simulate."; return; }
            if (activeSimulationWindow != null)
            {
                activeSimulationWindow.Activate();
                if (startImmediately) activeSimulationWindow.StartFromMcp();
                return;
            }
            try
            {
                LiveSimulationWindow win = new LiveSimulationWindow(canvas.Schematic, settings);
                activeSimulationWindow = win;
                win.Closed += (_, _) => { if (ReferenceEquals(activeSimulationWindow, win)) activeSimulationWindow = null; };
                win.Show(this);
                if (startImmediately) win.StartFromMcp();
            }
            catch (Exception ex)
            {
                statusText.Text = "Open simulation failed: " + ex.Message;
            }
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
