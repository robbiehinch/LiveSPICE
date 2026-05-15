using System;
using Audio;

namespace CoreAudio
{
    public sealed class Channel : Audio.Channel
    {
        private readonly string name;
        public Channel(string name) { this.name = name; }
        public override string Name => name;
    }

    public sealed class Device : Audio.Device
    {
        // Opaque per-device identifier from miniaudio. Held until Open() is called.
        private readonly IntPtr deviceId;

        internal Device(string name, IntPtr deviceId, Channel[] inputs, Channel[] outputs)
            : base(name)
        {
            this.deviceId = deviceId;
            this.inputs = inputs;
            this.outputs = outputs;
        }

        public override Audio.Stream Open(Audio.Stream.SampleHandler callback, Audio.Channel[] input, Audio.Channel[] output)
        {
            // TODO: build an ma_device_config (duplex) with this device's id as both
            // capture and playback id, install a managed-to-native trampoline that
            // converts the ma_uint32 frameCount + float* buffers into a SampleBuffer[]
            // pair and invokes `callback`, then call ma_device_init/start.
            //
            // Returns a CoreAudio.Stream that owns the native ma_device handle.
            throw new NotImplementedException(
                "CoreAudio.Device.Open: pending native miniaudio bindings + binary bundling.");
        }
    }
}
