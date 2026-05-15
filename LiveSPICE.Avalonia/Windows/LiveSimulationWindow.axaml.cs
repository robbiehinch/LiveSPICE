using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveSPICE.Avalonia.Controls;
using LiveSPICE.Avalonia.Services;
using SchematicControls.Editor.Tools;
using Util;

namespace LiveSPICE.Avalonia.Windows
{
    public partial class LiveSimulationWindow : Window
    {
        private readonly Circuit.Schematic sourceSchematic;
        private readonly Settings settings;
        private LiveSimulationService service;
        private DispatcherTimer refresh;

        public LiveSimulationWindow() : this(null, Settings.Load()) { }

        public LiveSimulationWindow(Circuit.Schematic schematic, Settings settings)
        {
            InitializeComponent();
            this.sourceSchematic = schematic;
            this.settings = settings;

            // Show the source schematic immediately so the user has visual context before
            // they hit Start. Once they Start, we swap to the simulation service's clone
            // (so probes added during simulation don't pollute their saved file).
            schematicView.Canvas.Schematic = schematic;
            schematicView.Canvas.Tool = new SelectionTool(schematicView.Canvas);
            UpdateToolStatus();

            PopulateDrivers();
            RestoreSettings();

            Closed += (_, _) => Cleanup();

            refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnRefresh);
            refresh.Start();
        }

        private void PopulateDrivers()
        {
            driverCombo.ItemsSource = Audio.Driver.Drivers.Select(d => d.Name).ToList();
        }

        private void RestoreSettings()
        {
            if (!string.IsNullOrEmpty(settings.AudioDriver))
            {
                int idx = (driverCombo.ItemsSource as IList<string>)?.IndexOf(settings.AudioDriver) ?? -1;
                if (idx >= 0) driverCombo.SelectedIndex = idx;
            }
            else if (driverCombo.ItemCount > 0) driverCombo.SelectedIndex = 0;
        }

        private Audio.Driver SelectedDriver =>
            Audio.Driver.Drivers.FirstOrDefault(d => d.Name == driverCombo.SelectedItem as string);

        private Audio.Device SelectedDevice =>
            SelectedDriver?.Devices.FirstOrDefault(d => d.Name == deviceCombo.SelectedItem as string);

        private void DriverChanged(object sender, SelectionChangedEventArgs e)
        {
            Audio.Driver d = SelectedDriver;
            if (d == null)
            {
                deviceCombo.ItemsSource = null;
                inputsList.ItemsSource = null;
                outputsList.ItemsSource = null;
                return;
            }
            deviceCombo.ItemsSource = d.Devices.Select(dev => dev.Name).ToList();
            string remembered = settings.AudioDevice;
            int devIdx = -1;
            if (!string.IsNullOrEmpty(remembered))
                devIdx = (deviceCombo.ItemsSource as IList<string>)?.IndexOf(remembered) ?? -1;
            if (devIdx >= 0) deviceCombo.SelectedIndex = devIdx;
            else if (deviceCombo.ItemCount > 0) deviceCombo.SelectedIndex = 0;
        }

        private void DeviceChanged(object sender, SelectionChangedEventArgs e)
        {
            Audio.Device dev = SelectedDevice;
            if (dev == null) { inputsList.ItemsSource = null; outputsList.ItemsSource = null; return; }
            inputsList.ItemsSource = dev.InputChannels.Select(c => c.Name).ToList();
            outputsList.ItemsSource = dev.OutputChannels.Select(c => c.Name).ToList();
            ApplyRememberedSelection(inputsList, settings.AudioInputs);
            ApplyRememberedSelection(outputsList, settings.AudioOutputs);
        }

        private static void ApplyRememberedSelection(ListBox list, List<string> remembered)
        {
            if (remembered == null || remembered.Count == 0) return;
            for (int i = 0; i < list.ItemCount; i++)
            {
                string item = list.ItemsSource is IList<string> src ? src[i] : null;
                if (item != null && remembered.Contains(item))
                    list.SelectedItems.Add(item);
            }
        }

        private void StartStopClicked(object sender, RoutedEventArgs e)
        {
            if (service != null) { StopSimulation(); return; }
            StartSimulation();
        }

        private void StartSimulation()
        {
            Audio.Device dev = SelectedDevice;
            Audio.Channel[] ins = SelectedChannels(inputsList, dev?.InputChannels);
            Audio.Channel[] outs = SelectedChannels(outputsList, dev?.OutputChannels);

            settings.AudioDriver = SelectedDriver?.Name;
            settings.AudioDevice = dev?.Name;
            settings.AudioInputs = ins.Select(c => c.Name).ToList();
            settings.AudioOutputs = outs.Select(c => c.Name).ToList();
            settings.Save();

            statusText.Text = "Building simulation...";
            try
            {
                service = new LiveSimulationService(sourceSchematic, dev, ins, outs, new NullLog())
                {
                    Oversample = (int)(oversampleBox.Value ?? 8),
                    Iterations = (int)(iterationsBox.Value ?? 8),
                };
                service.SolutionBuilt += () => Dispatcher.UIThread.Post(() => statusText.Text = "Running at " + service.SampleRate + " Hz.");
                service.SimulationFault += ex => Dispatcher.UIThread.Post(() => statusText.Text = "Fault: " + ex.Message);
                service.Start();

                // Swap the visible schematic to the simulation's clone so the user's edits
                // (e.g. dropped probes) only affect the running simulation, not the source.
                schematicView.Canvas.Schematic = service.Schematic;
                schematicView.Canvas.Tool = new SelectionTool(schematicView.Canvas);
                UpdateToolStatus();

                startStopButton.Content = "Stop";
            }
            catch (Exception ex)
            {
                statusText.Text = "Failed to start: " + ex.Message;
                service = null;
            }
        }

        private void StopSimulation()
        {
            if (service == null) return;
            try { service.Stop(); } catch { }
            service = null;
            startStopButton.Content = "Start";
            statusText.Text = "Stopped.";
            inMeter.Value = 0;
            outMeter.Value = 0;
            scope.SetTraces(null);

            // Re-bind to the source schematic (so probes dropped during the previous run
            // are gone — they only lived on the clone).
            schematicView.Canvas.Schematic = sourceSchematic;
            schematicView.Canvas.Tool = new SelectionTool(schematicView.Canvas);
            UpdateToolStatus();
        }

        private static Audio.Channel[] SelectedChannels(ListBox list, Audio.Channel[] all)
        {
            if (all == null) return Array.Empty<Audio.Channel>();
            List<Audio.Channel> picks = new List<Audio.Channel>();
            foreach (object o in list.SelectedItems ?? Array.Empty<object>())
            {
                if (o is string s)
                {
                    Audio.Channel ch = all.FirstOrDefault(c => c.Name == s);
                    if (ch != null) picks.Add(ch);
                }
            }
            return picks.ToArray();
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            if (service == null || !service.IsRunning) return;
            double inPeak = service.InputPeaks.Length == 0 ? 0 : service.InputPeaks.Max();
            double outPeak = service.OutputPeaks.Length == 0 ? 0 : service.OutputPeaks.Max();
            inMeter.Value = Math.Min(1.0, inPeak);
            outMeter.Value = Math.Min(1.0, outPeak);

            List<ScopeTrace> traces = new List<ScopeTrace>
            {
                new ScopeTrace("Out", service.SnapshotMaster(), Color.FromRgb(0x4f, 0xc1, 0xff)),
            };
            foreach (Circuit.Probe p in service.Probes)
            {
                traces.Add(new ScopeTrace(
                    p.V.ToString(),
                    service.SnapshotProbe(p),
                    EdgeColorToAvalonia(p.Color)));
            }
            scope.SetTraces(traces, 1.0);
        }

        private static Color EdgeColorToAvalonia(Circuit.EdgeType e) => e switch
        {
            Circuit.EdgeType.Red => Color.FromRgb(255, 80, 80),
            Circuit.EdgeType.Green => Color.FromRgb(80, 220, 80),
            Circuit.EdgeType.Blue => Color.FromRgb(20, 180, 255),
            Circuit.EdgeType.Yellow => Color.FromRgb(240, 220, 80),
            Circuit.EdgeType.Cyan => Color.FromRgb(80, 220, 220),
            Circuit.EdgeType.Magenta => Color.FromRgb(220, 80, 220),
            Circuit.EdgeType.Orange => Color.FromRgb(240, 160, 80),
            Circuit.EdgeType.Gray => Color.FromRgb(180, 180, 180),
            Circuit.EdgeType.Black => Color.FromRgb(40, 40, 40),
            _ => Color.FromRgb(180, 180, 180),
        };

        private void ToolSelectionClicked(object sender, RoutedEventArgs e)
        {
            schematicView.Canvas.Tool = new SelectionTool(schematicView.Canvas);
            UpdateToolStatus();
        }

        private void ToolProbeClicked(object sender, RoutedEventArgs e)
        {
            if (schematicView.Canvas.Schematic == null) return;
            schematicView.Canvas.Tool = new SymbolTool(schematicView.Canvas, new Circuit.Probe(NextProbeColor()));
            UpdateToolStatus();
        }

        // Cycle through a palette so successive probes get distinct colors.
        private static readonly Circuit.EdgeType[] ProbePalette = new[]
        {
            Circuit.EdgeType.Magenta, Circuit.EdgeType.Green, Circuit.EdgeType.Yellow,
            Circuit.EdgeType.Cyan, Circuit.EdgeType.Orange, Circuit.EdgeType.Red, Circuit.EdgeType.Blue,
        };

        private Circuit.EdgeType NextProbeColor()
        {
            int existing = service?.Probes.Count ?? 0;
            // Also count probes on the static (pre-Start) schematic.
            if (existing == 0 && schematicView.Canvas.Schematic != null)
                existing = schematicView.Canvas.Schematic.Elements.OfType<Circuit.Symbol>()
                    .Count(s => s.Component is Circuit.Probe);
            return ProbePalette[existing % ProbePalette.Length];
        }

        private void UpdateToolStatus()
        {
            string n = schematicView.Canvas.Tool?.GetType().Name ?? "(none)";
            toolStatus.Text = "(" + n + ")";
        }

        private void Cleanup()
        {
            refresh?.Stop();
            refresh = null;
            StopSimulation();
        }
    }
}
