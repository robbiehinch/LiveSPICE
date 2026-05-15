using System;
using System.Runtime.InteropServices;

namespace CoreAudio.Native
{
    /// <summary>
    /// Thin P/Invoke bindings to a subset of the <a href="https://miniaud.io/">miniaudio</a>
    /// C API. Only the device-lifecycle and duplex-callback surface is bound — enough to
    /// run a single <see cref="CoreAudio.Stream"/>.
    ///
    /// The native binary must be present alongside the managed assembly. On macOS that
    /// means <c>runtimes/osx-{arm64,x64}/native/libminiaudio.dylib</c>. The .dylib itself
    /// is not checked into the repo yet — see the plan's Tier 3 open question for the
    /// bundling decision.
    /// </summary>
    internal static class MiniAudio
    {
        private const string LibraryName = "miniaudio";

        // Subset of ma_device_type. Capture+playback gives us a single duplex device,
        // which matches the LiveSPICE SampleHandler(in[], out[]) contract.
        public enum DeviceType : int
        {
            Playback = 1,
            Capture = 2,
            Duplex = Playback | Capture,
            Loopback = 4,
        }

        public enum Format : int
        {
            Unknown = 0,
            U8 = 1,
            S16 = 2,
            S24 = 3,
            S32 = 4,
            F32 = 5,
        }

        public enum Result : int
        {
            Success = 0,
            // miniaudio returns a much wider error enum; non-zero is just "failure".
        }

        /// <summary>
        /// Native miniaudio callback signature:
        /// <c>void on_data(ma_device* pDevice, void* pOutput, const void* pInput, ma_uint32 frameCount);</c>
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DataCallback(IntPtr device, IntPtr output, IntPtr input, uint frameCount);

        // The real ma_device_config and ma_device structs are large (hundreds of bytes) and
        // version-sensitive. A production binding should mirror the exact layout from the
        // miniaudio.h header bundled with the .dylib. For scaffolding purposes we expose
        // an opaque handle and rely on a thin C shim (planned) that returns the configured
        // pointer.
        [StructLayout(LayoutKind.Sequential)]
        public struct DeviceConfig
        {
            public DeviceType DeviceType;
            public uint SampleRate;
            public uint PeriodSizeInFrames;
            public Format CaptureFormat;
            public uint CaptureChannels;
            public Format PlaybackFormat;
            public uint PlaybackChannels;
            public IntPtr CaptureDeviceId;     // ma_device_id*
            public IntPtr PlaybackDeviceId;    // ma_device_id*
            public IntPtr DataCallback;        // function pointer
            public IntPtr UserData;
        }

        // ---- Lifecycle ----
        //
        // These signatures correspond to the public miniaudio API. They will not bind on
        // Windows (LibraryName "miniaudio" has no Windows binary in this project); the
        // driver is only instantiated when running on macOS — see Driver.IsSupported.

        [DllImport(LibraryName, EntryPoint = "ma_context_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextInit(IntPtr backends, uint backendCount, IntPtr config, IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextUninit(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_get_devices", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextGetDevices(
            IntPtr context,
            out IntPtr pPlaybackInfos, out uint playbackCount,
            out IntPtr pCaptureInfos, out uint captureCount);

        [DllImport(LibraryName, EntryPoint = "ma_device_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceInit(IntPtr context, ref DeviceConfig config, IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeviceUninit(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_start", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceStart(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_stop", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceStop(IntPtr device);
    }
}
