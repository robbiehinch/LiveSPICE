using System;
using System.Threading;

namespace LiveSPICE.Avalonia.Services
{
    /// <summary>
    /// Audio stream that produces silence and fires the callback at a fixed sample rate.
    /// Used when no real audio device is selected so simulation/scope still tick over.
    /// Direct port of <c>LiveSPICE.NullStream</c>; behaviour identical.
    /// </summary>
    public sealed class NullStream : Audio.Stream
    {
        public override double SampleRate => 48000;

        private readonly SampleHandler callback;
        private volatile bool run = true;
        private Thread thread;

        public NullStream(SampleHandler callback) : base(new Audio.Channel[0], new Audio.Channel[0])
        {
            this.callback = callback;
            thread = new Thread(Loop) { IsBackground = true, Name = "LiveSPICE NullStream" };
            thread.Start();
        }

        private void Loop()
        {
            Audio.SampleBuffer[] input = Array.Empty<Audio.SampleBuffer>();
            Audio.SampleBuffer[] output = Array.Empty<Audio.SampleBuffer>();

            long sentSamples = 0;
            DateTime start = DateTime.Now;
            while (run)
            {
                Thread.Sleep(20);
                double elapsed = (DateTime.Now - start).TotalSeconds;
                int need = (int)(Math.Round(elapsed * SampleRate) - sentSamples);
                if (need <= 0) continue;
                try { callback(need, input, output, SampleRate); }
                catch { /* keep ticking; the host UI is responsible for surfacing errors. */ }
                sentSamples += need;
            }
        }

        public override void Stop()
        {
            run = false;
            thread?.Join();
            thread = null;
        }
    }
}
