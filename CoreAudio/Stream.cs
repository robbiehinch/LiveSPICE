using System;

namespace CoreAudio
{
    public sealed class Stream : Audio.Stream
    {
        private readonly double sampleRate;
        // Native ma_device* — pinned managed allocation owned for the stream lifetime.
        private IntPtr deviceHandle;

        internal Stream(Audio.Channel[] inputs, Audio.Channel[] outputs, double sampleRate, IntPtr deviceHandle)
            : base(inputs, outputs)
        {
            this.sampleRate = sampleRate;
            this.deviceHandle = deviceHandle;
        }

        public override double SampleRate => sampleRate;

        public override void Stop()
        {
            if (deviceHandle == IntPtr.Zero) return;
            Native.MiniAudio.DeviceStop(deviceHandle);
            Native.MiniAudio.DeviceUninit(deviceHandle);
            deviceHandle = IntPtr.Zero;
        }
    }
}
