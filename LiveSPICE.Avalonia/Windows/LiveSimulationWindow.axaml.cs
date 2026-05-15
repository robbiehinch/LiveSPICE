using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveSPICE.Avalonia.Services;
using Util;

namespace LiveSPICE.Avalonia.Windows
{
    public partial class LiveSimulationWindow : Window
    {
        private readonly Circuit.Schematic schematic;
        private readonly Settings settings;
        private LiveSimulationService service;
        private DispatcherTimer refresh;

        /// <summary>Design-time only constructor — the real entry point is the overload below.</summary>
        public LiveSimulationWindow() : this(null, Settings.Load()) { }

        public LiveSimulationWindow(Circuit.Schematic schematic, Settings settings)
        {
            InitializeComponent();
            this.schematic = schematic;
            this.settings = settings;

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
                service = new LiveSimulationService(schematic, dev, ins, outs, new NullLog())
                {
                    Oversample = (int)(oversampleBox.Value ?? 8),
                    Iterations = (int)(iterationsBox.Value ?? 8),
                };
                service.SolutionBuilt += () => Dispatcher.UIThread.Post(() => statusText.Text = "Running at " + service.SampleRate + " Hz.");
                service.SimulationFault += ex => Dispatcher.UIThread.Post(() => statusText.Text = "Fault: " + ex.Message);
                service.Start();
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
            try { service.Stop(); } catch { /* swallow */ }
            service = null;
            startStopButton.Content = "Start";
            statusText.Text = "Stopped.";
            inMeter.Value = 0;
            outMeter.Value = 0;
            scope.SetTrace(Array.Empty<double>());
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
            scope.SetTrace(service.SnapshotScope(), 1.0);
        }

        private void Cleanup()
        {
            refresh?.Stop();
            refresh = null;
            StopSimulation();
        }
    }
}
