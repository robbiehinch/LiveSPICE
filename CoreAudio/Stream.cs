using System;
using System.Runtime.InteropServices;
using Audio;
using CoreAudio.Native;
using Util;

namespace CoreAudio
{
    /// <summary>
    /// A running duplex audio stream over miniaudio.
    /// Holds the native ls_device* and a managed trampoline that demuxes/muxes the
    /// interleaved float buffers miniaudio hands us into the per-channel
    /// <see cref="SampleBuffer"/> array LiveSPICE expects.
    /// </summary>
    public sealed class Stream : Audio.Stream
    {
        private IntPtr deviceHandle;
        private readonly double sampleRate;
        private readonly Channel[] inputChannels;
        private readonly Channel[] outputChannels;
        private readonly SampleHandler callback;

        // Keep the unmanaged callback alive for the device lifetime so the GC doesn't
        // collect the delegate while miniaudio still has the function pointer.
        private readonly MiniAudio.DataCallback nativeCallback;

        // Per-stream SampleBuffer arrays — reallocated when the frame count changes.
        private SampleBuffer[] inputBuffers;
        private SampleBuffer[] outputBuffers;
        private int bufferFrames;

        internal Stream(
            SampleHandler callback,
            IntPtr context,
            IntPtr captureId,  Channel[] inputs,
            IntPtr playbackId, Channel[] outputs)
            : base(inputs, outputs)
        {
            this.callback       = callback;
            this.inputChannels  = inputs;
            this.outputChannels = outputs;
            this.nativeCallback = OnAudioData;

            // miniaudio insists on at least one frame's worth of channels per side; mute
            // sides the user didn't request by passing 0 channels with a null id.
            int captureChannelCount  = MaxIndex(inputs)  + 1;
            int playbackChannelCount = MaxIndex(outputs) + 1;

            const int requestedSampleRate = 48000;
            const int requestedPeriod     = 0; // 0 = let miniaudio pick a sensible default.

            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(nativeCallback);

            deviceHandle = MiniAudio.ls_device_create(
                context,
                captureId,  captureChannelCount,
                playbackId, playbackChannelCount,
                requestedSampleRate,
                requestedPeriod,
                callbackPtr,
                IntPtr.Zero);

            if (deviceHandle == IntPtr.Zero)
                throw new InvalidOperationException("CoreAudio.Stream: ls_device_create failed.");

            sampleRate = MiniAudio.ls_device_sample_rate(deviceHandle);

            int startResult = MiniAudio.ls_device_start(deviceHandle);
            if (startResult != 0)
            {
                MiniAudio.ls_device_destroy(deviceHandle);
                deviceHandle = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"CoreAudio.Stream: ls_device_start failed (result {startResult}).");
            }
        }

        public override double SampleRate => sampleRate;

        public override void Stop()
        {
            if (deviceHandle == IntPtr.Zero) return;
            MiniAudio.ls_device_stop(deviceHandle);
            MiniAudio.ls_device_destroy(deviceHandle);
            deviceHandle = IntPtr.Zero;

            if (inputBuffers != null) foreach (var b in inputBuffers)  b?.Dispose();
            if (outputBuffers != null) foreach (var b in outputBuffers) b?.Dispose();
        }

        /// <summary>
        /// Audio-thread callback.  Deinterleave native floats → per-channel double
        /// SampleBuffers, hand them to the user callback, then interleave back.
        /// Runs on miniaudio's real-time thread, so it must not allocate beyond the
        /// per-frame-size resize and must catch its own exceptions.
        /// </summary>
        private void OnAudioData(
            IntPtr userdata,
            IntPtr input,  int inputCh,
            IntPtr output, int outputCh,
            int frameCount)
        {
            try
            {
                EnsureBuffers(frameCount);

                // Deinterleave inputs.  miniaudio sends f32 interleaved by channel;
                // LiveSPICE consumes doubles per channel.
                if (input != IntPtr.Zero && inputBuffers.Length > 0)
                {
                    Deinterleave(input, inputCh, inputBuffers, inputChannels, frameCount);
                }

                // Zero outputs first so the callback can OR-blend if it wants to; the
                // LiveSPICE simulation overwrites each frame anyway, but other handlers
                // may not.
                if (outputBuffers != null)
                    foreach (var b in outputBuffers) b.Clear();

                callback(frameCount, inputBuffers, outputBuffers, sampleRate);

                // Interleave outputs.
                if (output != IntPtr.Zero && outputBuffers.Length > 0)
                {
                    Interleave(outputBuffers, outputChannels, output, outputCh, frameCount);
                }
                else if (output != IntPtr.Zero)
                {
                    // Capture-only callbacks may still hand us an output buffer; zero it.
                    Audio.Util.ZeroMemory(output, (uint)(frameCount * outputCh * sizeof(float)));
                }
            }
            catch (Exception ex)
            {
                Log.Global.WriteLine(MessageType.Error, "CoreAudio audio-thread exception: {0}", ex);
            }
        }

        private void EnsureBuffers(int frameCount)
        {
            if (bufferFrames == frameCount && inputBuffers != null && outputBuffers != null)
                return;

            if (inputBuffers != null) foreach (var b in inputBuffers)  b?.Dispose();
            if (outputBuffers != null) foreach (var b in outputBuffers) b?.Dispose();

            inputBuffers  = new SampleBuffer[inputChannels.Length];
            for (int i = 0; i < inputBuffers.Length; ++i) inputBuffers[i] = new SampleBuffer(frameCount);

            outputBuffers = new SampleBuffer[outputChannels.Length];
            for (int i = 0; i < outputBuffers.Length; ++i) outputBuffers[i] = new SampleBuffer(frameCount);

            bufferFrames = frameCount;
        }

        private static unsafe void Deinterleave(
            IntPtr src, int srcChannels,
            SampleBuffer[] dst, Channel[] dstChannels,
            int frameCount)
        {
            float* p = (float*)src;
            for (int c = 0; c < dst.Length; ++c)
            {
                int srcIdx = dstChannels[c].Index;
                if (srcIdx < 0 || srcIdx >= srcChannels)
                {
                    dst[c].Clear();
                    continue;
                }
                double* d = (double*)dst[c].Raw;
                for (int i = 0; i < frameCount; ++i)
                    d[i] = p[i * srcChannels + srcIdx];
            }
        }

        private static unsafe void Interleave(
            SampleBuffer[] src, Channel[] srcChannels,
            IntPtr dst, int dstChannels,
            int frameCount)
        {
            float* p = (float*)dst;
            // The caller already zeroed the buffer; we OR-blend any of the selected
            // channels we have samples for.
            for (int c = 0; c < src.Length; ++c)
            {
                int dstIdx = srcChannels[c].Index;
                if (dstIdx < 0 || dstIdx >= dstChannels) continue;
                double* s = (double*)src[c].Raw;
                for (int i = 0; i < frameCount; ++i)
                    p[i * dstChannels + dstIdx] = (float)s[i];
            }
        }

        private static int MaxIndex(Channel[] channels)
        {
            int m = -1;
            foreach (var c in channels) if (c.Index > m) m = c.Index;
            return m;
        }
    }
}
