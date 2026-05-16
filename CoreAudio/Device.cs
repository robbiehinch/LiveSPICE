using System;
using System.Linq;
using Audio;

namespace CoreAudio
{
    /// <summary>
    /// One physical channel on a <see cref="Device"/>.  Carries the zero-based
    /// channel index so the duplex stream knows which slot of miniaudio's
    /// interleaved buffer to read/write.
    /// </summary>
    public sealed class Channel : Audio.Channel
    {
        public Channel(string name, int index) { Name_ = name; Index = index; }

        public int Index { get; }
        private string Name_;
        public override string Name => Name_;
    }

    public sealed class Device : Audio.Device
    {
        private readonly IntPtr context;
        private readonly IntPtr captureId;
        private readonly IntPtr playbackId;

        internal Device(
            string name,
            IntPtr context,
            IntPtr captureId,  Channel[] inputs,
            IntPtr playbackId, Channel[] outputs)
            : base(name)
        {
            this.context    = context;
            this.captureId  = captureId;
            this.playbackId = playbackId;
            this.inputs     = inputs;
            this.outputs    = outputs;
        }

        public override Audio.Stream Open(
            Audio.Stream.SampleHandler callback,
            Audio.Channel[] input,
            Audio.Channel[] output)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            // The user picks Channel objects out of the InputChannels / OutputChannels
            // lists; pass them straight through to the Stream so it can demux/mux the
            // miniaudio interleaved buffers using each channel's stored index.
            var inputCh  = input ?.Cast<Channel>().ToArray() ?? Array.Empty<Channel>();
            var outputCh = output?.Cast<Channel>().ToArray() ?? Array.Empty<Channel>();

            return new Stream(
                callback,
                context,
                inputCh.Length  > 0 ? captureId  : IntPtr.Zero, inputCh,
                outputCh.Length > 0 ? playbackId : IntPtr.Zero, outputCh);
        }
    }
}
