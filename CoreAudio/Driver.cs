using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CoreAudio.Native;
using Util;

namespace CoreAudio
{
    /// <summary>
    /// macOS audio driver, discovered by reflection from <see cref="Audio.Driver.Drivers"/>.
    ///
    /// Wraps miniaudio's CoreAudio backend rather than calling AudioUnit / AudioObject
    /// directly.  A single per-driver miniaudio context is reused for every device it
    /// enumerates.
    /// </summary>
    public sealed class Driver : Audio.Driver, IDisposable
    {
        public override string Name => "CoreAudio";

        internal IntPtr Context { get; private set; } = IntPtr.Zero;

        public Driver()
        {
            if (!IsSupported())
                return;

            try
            {
                Context = MiniAudio.ls_context_create();
                if (Context == IntPtr.Zero)
                {
                    Log.Global.WriteLine(MessageType.Error, "CoreAudio: ls_context_create failed.");
                    return;
                }

                EnumerateDevices();
            }
            catch (DllNotFoundException ex)
            {
                Log.Global.WriteLine(MessageType.Error,
                    "CoreAudio: libminiaudio.dylib not found ({0}). The CoreAudio backend is disabled.",
                    ex.Message);
            }
            catch (Exception ex)
            {
                Log.Global.WriteLine(MessageType.Error, "CoreAudio: driver init failed: {0}", ex);
            }
        }

        private void EnumerateDevices()
        {
            if (MiniAudio.ls_context_enumerate(
                    Context,
                    out IntPtr playbackInfos, out int playbackCount,
                    out IntPtr captureInfos,  out int captureCount) != 0)
            {
                Log.Global.WriteLine(MessageType.Warning, "CoreAudio: ls_context_enumerate failed.");
                return;
            }

            // miniaudio returns parallel playback/capture info arrays — they're separate
            // logical endpoints (a USB interface usually shows up in both).  LiveSPICE's
            // Audio.Device exposes a single device with input + output channel lists, so
            // synthesise one Device per unique name and bucket the channels accordingly.
            var byName  = new Dictionary<string, DeviceEntry>(StringComparer.Ordinal);
            int infoSize = (int)MiniAudio.ls_device_info_size().ToUInt32();

            void Collect(IntPtr arr, int count, bool isPlayback)
            {
                for (int i = 0; i < count; ++i)
                {
                    IntPtr info    = arr + i * infoSize;
                    IntPtr namePtr = MiniAudio.ls_device_info_name(info);
                    string name    = Marshal.PtrToStringUTF8(namePtr) ?? $"(device {i})";

                    if (MiniAudio.ls_context_get_device_channels(
                            Context,
                            isPlayback ? MiniAudio.DeviceType.Playback : MiniAudio.DeviceType.Capture,
                            MiniAudio.ls_device_info_id(info),
                            out int channels) != 0)
                    {
                        channels = isPlayback ? 2 : 1;
                    }

                    if (!byName.TryGetValue(name, out DeviceEntry entry))
                    {
                        entry = new DeviceEntry { Name = name };
                        byName.Add(name, entry);
                    }

                    if (isPlayback)
                    {
                        entry.PlaybackIdPtr   = MiniAudio.ls_device_info_id(info);
                        entry.PlaybackChannels = channels;
                    }
                    else
                    {
                        entry.CaptureIdPtr    = MiniAudio.ls_device_info_id(info);
                        entry.CaptureChannels = channels;
                    }
                }
            }

            Collect(playbackInfos, playbackCount, isPlayback: true);
            Collect(captureInfos,  captureCount,  isPlayback: false);

            foreach (var entry in byName.Values)
            {
                var inputs  = BuildChannels(entry.Name, entry.CaptureChannels,  "Input");
                var outputs = BuildChannels(entry.Name, entry.PlaybackChannels, "Output");
                devices.Add(new Device(
                    entry.Name,
                    Context,
                    entry.CaptureIdPtr,  inputs,
                    entry.PlaybackIdPtr, outputs));
            }
        }

        private static Channel[] BuildChannels(string deviceName, int count, string role)
        {
            if (count <= 0) return Array.Empty<Channel>();
            var arr = new Channel[count];
            for (int i = 0; i < count; ++i)
                arr[i] = new Channel($"{deviceName} {role} {i + 1}", i);
            return arr;
        }

        public void Dispose()
        {
            if (Context != IntPtr.Zero)
            {
                MiniAudio.ls_context_destroy(Context);
                Context = IntPtr.Zero;
            }
        }

        internal static bool IsSupported() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private sealed class DeviceEntry
        {
            public string Name;
            public IntPtr CaptureIdPtr;
            public IntPtr PlaybackIdPtr;
            public int    CaptureChannels;
            public int    PlaybackChannels;
        }
    }
}
