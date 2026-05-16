using System;
using System.Runtime.InteropServices;

namespace CoreAudio.Native
{
    /// <summary>
    /// P/Invoke bindings against the LiveSPICE-side shim (<c>livespice_miniaudio.c</c>)
    /// over <a href="https://miniaud.io/">miniaudio</a>.
    ///
    /// We talk to miniaudio through a thin C shim rather than the raw
    /// <c>ma_device_config</c> / <c>ma_device</c> structs because those are large
    /// (hundreds of bytes), version-sensitive, and contain unions that would
    /// require constant re-checking against the bundled <c>miniaudio.h</c>. The
    /// shim hides all that behind opaque pointers.
    /// </summary>
    internal static class MiniAudio
    {
        private const string LibraryName = "miniaudio";

        // Mirrors ma_device_type. Stable across miniaudio versions.
        public enum DeviceType : int
        {
            Playback = 1,
            Capture  = 2,
            Duplex   = Playback | Capture,
            Loopback = 4,
        }

        /// <summary>
        /// Native data-callback signature, called on the audio thread.
        /// Input pointer may be null if the device is playback-only.
        /// Output pointer may be null if the device is capture-only.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DataCallback(
            IntPtr userdata,
            IntPtr input,  int inputChannels,
            IntPtr output, int outputChannels,
            int frameCount);

        // ---- Context ----
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ls_context_create();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ls_context_destroy(IntPtr context);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_context_enumerate(
            IntPtr context,
            out IntPtr playbackInfos, out int playbackCount,
            out IntPtr captureInfos,  out int captureCount);

        // ---- Device-info accessors. ----
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ls_device_info_name(IntPtr info);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ls_device_info_id(IntPtr info);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UIntPtr ls_device_info_size();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_context_get_device_channels(
            IntPtr context,
            DeviceType type,
            IntPtr deviceId,
            out int channels);

        // ---- Device / stream ----
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ls_device_create(
            IntPtr context,
            IntPtr captureId,  int captureChannels,
            IntPtr playbackId, int playbackChannels,
            int sampleRate,
            int periodFrames,
            IntPtr dataCallback,
            IntPtr userdata);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_device_start(IntPtr device);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_device_stop(IntPtr device);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ls_device_destroy(IntPtr device);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_device_sample_rate(IntPtr device);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_device_capture_channels(IntPtr device);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ls_device_playback_channels(IntPtr device);
    }
}
