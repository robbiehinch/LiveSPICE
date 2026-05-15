using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Circuit;
using ComputerAlgebra;
using Util;

namespace LiveSPICE.Avalonia.Services
{
    /// <summary>
    /// Owns a running circuit simulation: builds the solution off the UI thread, opens the
    /// audio stream, and pumps input/output buffers through <see cref="Simulation.Run"/> on
    /// the audio thread. Tracks probe components placed on the cloned schematic and feeds
    /// their voltage samples into per-probe ring buffers for the scope.
    /// </summary>
    public sealed class LiveSimulationService : IDisposable
    {
        static LiveSimulationService()
        {
            ForceLoad(typeof(WaveAudio.Driver));
            if (OperatingSystem.IsWindows())
                ForceLoad(typeof(Asio.Driver));
        }

        private static void ForceLoad(Type t) { _ = t.FullName; }

        private readonly Schematic source;
        private readonly Audio.Device device;
        private readonly Audio.Channel[] inputChannels;
        private readonly Audio.Channel[] outputChannels;
        private readonly ILog log;

        private readonly object sync = new object();
        private readonly List<double[]> inputBuffers = new List<double[]>();
        private readonly List<double[]> outputBuffers = new List<double[]>();

        private Schematic clone;
        private Circuit.Circuit circuit;
        private Simulation simulation;
        private Probe[] simulationProbes = Array.Empty<Probe>();
        private Audio.Stream stream;
        private Expression speakerMix = (Expression)0;
        private readonly List<Expression> inputExprs = new List<Expression>();
        private readonly List<Probe> probes = new List<Probe>();
        private readonly Dictionary<Probe, double[]> probeBuffers = new Dictionary<Probe, double[]>();

        public int Oversample { get; set; } = 8;
        public int Iterations { get; set; } = 8;
        public double InputGain { get; set; } = 1.0;
        public double OutputGain { get; set; } = 1.0;

        public double[] InputPeaks { get; private set; }
        public double[] OutputPeaks { get; private set; }

        private double[] masterScope;
        private int masterHead;

        public event Action<Exception> SimulationFault;
        public event Action SolutionBuilt;
        public event Action ProbesChanged;

        public LiveSimulationService(
            Schematic schematic,
            Audio.Device device,
            Audio.Channel[] inputs,
            Audio.Channel[] outputs,
            ILog log)
        {
            this.source = schematic ?? throw new ArgumentNullException(nameof(schematic));
            this.device = device;
            this.inputChannels = inputs ?? Array.Empty<Audio.Channel>();
            this.outputChannels = outputs ?? Array.Empty<Audio.Channel>();
            this.log = log ?? new NullLog();

            InputPeaks = new double[this.inputChannels.Length];
            OutputPeaks = new double[this.outputChannels.Length];
        }

        public Schematic Schematic => clone;

        public IReadOnlyList<Probe> Probes
        {
            get { lock (sync) return probes.ToArray(); }
        }

        public Audio.Stream Stream => stream;
        public double SampleRate => stream?.SampleRate ?? 0;
        public bool IsRunning => stream != null;

        public void Start(int scopeBufferSamples = 4096)
        {
            masterScope = new double[Math.Max(256, scopeBufferSamples)];
            masterHead = 0;

            clone = Schematic.Deserialize(source.Serialize(), log);
            clone.Elements.ItemAdded += OnElementAdded;
            clone.Elements.ItemRemoved += OnElementRemoved;

            circuit = clone.Build(log);

            inputExprs.Clear();
            speakerMix = (Expression)0;
            probes.Clear();
            probeBuffers.Clear();

            foreach (Component c in circuit.Components)
            {
                if (c is Input input) inputExprs.Add(input.In);
                if (c is Speaker spk) speakerMix += spk.Out;
                if (c is Probe p) { probes.Add(p); probeBuffers[p] = new double[masterScope.Length]; }
            }

            if (device != null && (inputChannels.Length > 0 || outputChannels.Length > 0))
                stream = device.Open(OnSamples, inputChannels, outputChannels);
            else
                stream = new NullStream(OnSamples);

            Task.Run(BuildSolution);
        }

        public void Stop()
        {
            Audio.Stream s;
            lock (sync) { s = stream; stream = null; simulation = null; simulationProbes = Array.Empty<Probe>(); }
            if (clone != null)
            {
                clone.Elements.ItemAdded -= OnElementAdded;
                clone.Elements.ItemRemoved -= OnElementRemoved;
            }
            try { s?.Stop(); } catch { }
        }

        public void Dispose() => Stop();

        public double[] SnapshotMaster() => SnapshotRing(masterScope, masterHead);

        public double[] SnapshotProbe(Probe p)
        {
            lock (sync)
            {
                if (!probeBuffers.TryGetValue(p, out double[] buf)) return Array.Empty<double>();
                return SnapshotRing(buf, masterHead);
            }
        }

        private static double[] SnapshotRing(double[] buf, int head)
        {
            if (buf == null) return Array.Empty<double>();
            double[] copy = new double[buf.Length];
            Array.Copy(buf, head, copy, 0, buf.Length - head);
            Array.Copy(buf, 0, copy, buf.Length - head, head);
            return copy;
        }

        private void OnElementAdded(object sender, ElementEventArgs e)
        {
            if (e.Element is Symbol sym && sym.Component is Probe p)
            {
                lock (sync)
                {
                    probes.Add(p);
                    if (!probeBuffers.ContainsKey(p))
                        probeBuffers[p] = new double[masterScope?.Length ?? 4096];
                    // Don't null `simulation` here — keep the old sim running with its old
                    // probe set until BuildSolution succeeds. Mismatch is handled by reading
                    // `simulationProbes` in OnSamples (not `probes`).
                }
                ProbesChanged?.Invoke();
                Task.Run(BuildSolution);
            }
        }

        private void OnElementRemoved(object sender, ElementEventArgs e)
        {
            if (e.Element is Symbol sym && sym.Component is Probe p)
            {
                lock (sync)
                {
                    probes.Remove(p);
                    probeBuffers.Remove(p);
                }
                ProbesChanged?.Invoke();
                Task.Run(BuildSolution);
            }
        }

        private void BuildSolution()
        {
            try
            {
                Expression h = (Expression)1 / (stream.SampleRate * Oversample);
                Probe[] snapshot;
                Expression[] outputs;
                lock (sync)
                {
                    snapshot = probes.ToArray();
                    outputs = new[] { speakerMix }.Concat(snapshot.Select(p => p.V)).ToArray();
                }
                TransientSolution solution = TransientSolution.Solve(circuit.Analyze(), h, log);
                Simulation sim = new Simulation(solution)
                {
                    Log = log,
                    Input = inputExprs.ToArray(),
                    Output = outputs,
                    Oversample = Oversample,
                    Iterations = Iterations,
                };
                lock (sync)
                {
                    simulation = sim;
                    simulationProbes = snapshot;
                }
                SolutionBuilt?.Invoke();
            }
            catch (Exception ex)
            {
                // Leave the previous simulation in place; just surface the fault.
                SimulationFault?.Invoke(ex);
            }
        }

        private void OnSamples(int count, Audio.SampleBuffer[] In, Audio.SampleBuffer[] Out, double rate)
        {
            for (int i = 0; i < In.Length && i < InputPeaks.Length; i++)
                InputPeaks[i] = In[i].Amplify(InputGain);

            Simulation sim;
            Probe[] simProbes;
            lock (sync)
            {
                sim = simulation;
                simProbes = simulationProbes;
            }

            if (sim == null)
            {
                foreach (Audio.SampleBuffer ob in Out) ob.Clear();
            }
            else
            {
                try
                {
                    if ((double)sim.SampleRate != rate)
                    {
                        lock (sync) simulation = null;
                        SimulationFault?.Invoke(new InvalidOperationException("Sample rate changed; restart simulation."));
                        foreach (Audio.SampleBuffer ob in Out) ob.Clear();
                        return;
                    }

                    inputBuffers.Clear();
                    for (int i = 0; i < inputExprs.Count; i++)
                    {
                        if (i < In.Length) inputBuffers.Add(In[i].Samples);
                        else inputBuffers.Add(new double[count]);
                    }

                    outputBuffers.Clear();
                    double[] master = new double[count];
                    outputBuffers.Add(master);
                    double[][] probeOut = new double[simProbes.Length][];
                    for (int i = 0; i < simProbes.Length; i++)
                    {
                        probeOut[i] = new double[count];
                        outputBuffers.Add(probeOut[i]);
                    }

                    sim.Run(count, inputBuffers, outputBuffers);

                    for (int i = 0; i < Out.Length; i++)
                        Array.Copy(master, Out[i].Samples, count);

                    lock (sync)
                    {
                        if (masterScope != null)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                masterScope[masterHead] = master[i];
                                for (int p = 0; p < simProbes.Length; p++)
                                {
                                    if (probeBuffers.TryGetValue(simProbes[p], out double[] pbuf) && pbuf.Length == masterScope.Length)
                                        pbuf[masterHead] = probeOut[p][i];
                                }
                                masterHead = (masterHead + 1) % masterScope.Length;
                            }
                        }
                    }
                }
                catch (SimulationDiverged ex)
                {
                    log.WriteLine(MessageType.Error, "Simulation diverged: " + ex.Message);
                    lock (sync) simulation = null;
                    SimulationFault?.Invoke(ex);
                    foreach (Audio.SampleBuffer ob in Out) ob.Clear();
                }
                catch (Exception ex)
                {
                    log.WriteLine(MessageType.Error, "Simulation error: " + ex.Message);
                    lock (sync) simulation = null;
                    SimulationFault?.Invoke(ex);
                    foreach (Audio.SampleBuffer ob in Out) ob.Clear();
                }
            }

            for (int i = 0; i < Out.Length && i < OutputPeaks.Length; i++)
                OutputPeaks[i] = Out[i].Amplify(OutputGain);
        }
    }
}
