using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using LiveSPICE.Avalonia.Controls;
using LiveSPICE.Avalonia.Services;
using LiveSPICE.Avalonia.Windows;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>
    /// Bridges the MCP tool handlers (which run on the HTTP listener's worker threads) to
    /// the live Avalonia state — the schematic canvas on the UI thread, and the simulation
    /// service when one's running. Tool handlers go through here so the rest of the MCP
    /// code doesn't have to know about <c>MainWindow</c>, <c>Dispatcher.UIThread</c>, or
    /// the cross-thread lifecycle of <c>LiveSimulationService</c>.
    /// </summary>
    public sealed class McpHost
    {
        private readonly Dictionary<Circuit.Element, int> ids = new Dictionary<Circuit.Element, int>();
        private int nextId = 1;
        private readonly object idSync = new object();

        public MainWindow MainWindow { get; }

        public McpHost(MainWindow mainWindow)
        {
            MainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public SchematicCanvas Canvas => MainWindow.Canvas;
        public LiveSimulationWindow SimulationWindow => MainWindow.ActiveSimulationWindow;
        public LiveSimulationService SimulationService => SimulationWindow?.ActiveService;

        public Task<T> OnUi<T>(Func<T> fn) => Dispatcher.UIThread.InvokeAsync(fn).GetTask();
        public Task OnUi(Action fn) => Dispatcher.UIThread.InvokeAsync(fn).GetTask();

        public int IdFor(Circuit.Element e)
        {
            lock (idSync)
            {
                if (ids.TryGetValue(e, out int existing)) return existing;
                int n = nextId++;
                ids[e] = n;
                return n;
            }
        }

        public bool TryResolve(int id, out Circuit.Element element)
        {
            lock (idSync)
            {
                foreach (KeyValuePair<Circuit.Element, int> kv in ids)
                {
                    if (kv.Value == id) { element = kv.Key; return true; }
                }
            }
            element = null;
            return false;
        }

        public void ForgetId(Circuit.Element e)
        {
            lock (idSync) { ids.Remove(e); }
        }
    }
}
