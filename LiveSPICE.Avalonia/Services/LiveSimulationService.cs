using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Circuit;
using ComputerAlgebra;
using Util;

namespace LiveSPICE.Avalonia.Services
{
    /// <summary>
    /// Owns a running circuit simulation: builds the solution off the UI thread, opens the
    /// audio stream, and pumps input/output buffers through <see cref="Simulation.Run"/> on
    /// the audio thread. Pure model — no UI dependencies, so it can be reused by any host.
    ///
    /// Lifetime: construct, call <see cref="Start"/>, eventually call <see cref="Stop"/>.
    /// </summary>
    public sealed class LiveSimulationService : IDisposable
    {
        static LiveSimulationService()
        {
            // Audio.Driver.Drivers uses reflection over AppDomain.CurrentDomain.GetAssemblies(),
            // which can miss assemblies that the runtime hasn't loaded yet. Touch each driver
            // type via typeof() so the assemblies are loaded before the first enumeration.
            ForceLoad(typeof(WaveAudio.Driver));
            if (OperatingSystem.IsWindows())
                ForceLoad(typeof(Asio.Driver));
        }

        private static void ForceLoad(Type t)
        {
            // Reference the type; nothing else needed.
            _ = t.FullName;
        }

        private readonly Schematic schematic;
        private readonly Audio.Device device;
        private readonly Audio.Channel[] inputChannels;
        private readonly Audio.Channel[] outputChannels;
        private readonly ILog log;

        private readonly object sync = new object();
        private readonly List<double[]> inputBuffers = new List<double[]>();
        private readonly List<double[]> outputBuffers = new List<double[]>();

        private Circuit.Circuit circuit;
        private Simulation simulation;
        private Audio.Stream stream;
        private Expression speakerMix = (Expression)0;
        private readonly List<Expression> inputExprs = new List<Expression>();

        public int Oversample { get; set; } = 8;
        public int Iterations { get; set; } = 8;
        public double InputGain { get; set; } = 1.0;
        public double OutputGain { get; set; } = 1.0;

        /// <summary>Most recent peak amplitude per input channel (atomic double via lock-free volatile).</summary>
        public double[] InputPeaks { get; private set; }
        /// <summary>Most recent peak amplitude per output channel.</summary>
        public double[] OutputPeaks { get; private set; }

        /// <summary>Last N samples of the master output mix, for the time-domain scope. Mutex-protected.</summary>
        private double[] scopeBuffer;
        private int scopeHead;
        public int ScopeCapacity => scopeBuffer?.Length ?? 0;

        public event Action<Exception> SimulationFault;
        public event Action SolutionBuilt;

        public LiveSimulationService(
            Schematic schematic,
            Audio.Device device,
            Audio.Channel[] inputs,
            Audio.Channel[] outputs,
            ILog log)
        {
            this.schematic = schematic ?? throw new ArgumentNullException(nameof(schematic));
            this.device = device; // may be null → falls back to NullStream
            this.inputChannels = inputs ?? Array.Empty<Audio.Channel>();
            this.outputChannels = outputs ?? Array.Empty<Audio.Channel>();
            this.log = log ?? new NullLog();

            InputPeaks = new double[this.inputChannels.Length];
            OutputPeaks = new double[this.outputChannels.Length];
        }

        public Audio.Stream Stream => stream;
        public double SampleRate => stream?.SampleRate ?? 0;
        public bool IsRunning => stream != null;

        /// <summary>
        /// Build the circuit, open the audio stream, and start pumping samples.
        /// </summary>
        public void Start(int scopeBufferSamples = 4096)
        {
            scopeBuffer = new double[Math.Max(256, scopeBufferSamples)];
            scopeHead = 0;

            // Build the circuit from the schematic on the calling thread; the solution build
            // happens asynchronously after the stream opens (it depends on SampleRate).
            Schematic clone = Schematic.Deserialize(schematic.Serialize(), log);
            circuit = clone.Build(log);

            // Discover input expressions and the master speaker mix.
            inputExprs.Clear();
            speakerMix = (Expression)0;
            foreach (Component c in circuit.Components)
            {
                if (c is Input input) inputExprs.Add(input.In);
                if (c is Speaker spk) speakerMix += spk.Out;
            }

            // Open the audio stream.
            if (device != null && (inputChannels.Length > 0 || outputChannels.Length > 0))
                stream = device.Open(OnSamples, inputChannels, outputChannels);
            else
                stream = new NullStream(OnSamples);

            // Build the solution on a background task — it depends on stream.SampleRate.
            Task.Run(BuildSolution);
        }

        public void Stop()
        {
            Audio.Stream s;
            lock (sync) { s = stream; stream = null; simulation = null; }
            try { s?.Stop(); } catch { /* swallow */ }
        }

        public void Dispose() => Stop();

        /// <summary>Copy a snapshot of the scope ring buffer for rendering.</summary>
        public double[] SnapshotScope()
        {
            if (scopeBuffer == null) return Array.Empty<double>();
            lock (sync)
            {
                double[] copy = new double[scopeBuffer.Length];
                int head = scopeHead;
                // Order oldest → newest into the output array.
                Array.Copy(scopeBuffer, head, copy, 0, scopeBuffer.Length - head);
                Array.Copy(scopeBuffer, 0, copy, scopeBuffer.Length - head, head);
                return copy;
            }
        }

        private void BuildSolution()
        {
            try
            {
                Expression h = (Expression)1 / (stream.SampleRate * Oversample);
                TransientSolution solution = TransientSolution.Solve(circuit.Analyze(), h, log);
                Simulation sim = new Simulation(solution)
                {
                    Log = log,
                    Input = inputExprs.ToArray(),
                    Output = new[] { speakerMix },
                    Oversample = Oversample,
                    Iterations = Iterations,
                };
                lock (sync) { simulation = sim; }
                SolutionBuilt?.Invoke();
            }
            catch (Exception ex)
            {
                SimulationFault?.Invoke(ex);
            }
        }

        private void OnSamples(int count, Audio.SampleBuffer[] In, Audio.SampleBuffer[] Out, double rate)
        {
            // Input gain + level meter
            for (int i = 0; i < In.Length && i < InputPeaks.Length; i++)
            {
                InputPeaks[i] = In[i].Amplify(InputGain);
            }

            Simulation sim;
            lock (sync) sim = simulation;

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
                        // Sample rate changed mid-stream — kill the solution and rebuild from
                        // the audio thread isn't safe; the foreground UI must restart us.
                        lock (sync) simulation = null;
                        SimulationFault?.Invoke(new InvalidOperationException("Sample rate changed; restart simulation."));
                        foreach (Audio.SampleBuffer ob in Out) ob.Clear();
                        return;
                    }

                    inputBuffers.Clear();
                    for (int i = 0; i < inputExprs.Count; i++)
                    {
                        if (i < In.Length) inputBuffers.Add(In[i].Samples);
                        else inputBuffers.Add(new double[count]); // missing channel → silence
                    }

                    outputBuffers.Clear();
                    double[] masterMix = new double[count];
                    outputBuffers.Add(masterMix);

                    sim.Run(count, inputBuffers, outputBuffers);

                    // Fan out master mix to all configured speaker output channels.
                    for (int i = 0; i < Out.Length; i++)
                    {
                        Array.Copy(masterMix, Out[i].Samples, count);
                    }

                    // Update scope ring buffer with master mix.
                    lock (sync)
                    {
                        if (scopeBuffer != null)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                scopeBuffer[scopeHead] = masterMix[i];
                                scopeHead = (scopeHead + 1) % scopeBuffer.Length;
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

            // Output gain + level meter
            for (int i = 0; i < Out.Length && i < OutputPeaks.Length; i++)
            {
                OutputPeaks[i] = Out[i].Amplify(OutputGain);
            }
        }
    }
}
