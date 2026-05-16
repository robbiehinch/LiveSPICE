using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WaveAudio
{
    public class Driver : Audio.Driver
    {
        public Driver()
        {
            // WaveAudio is a thin wrapper around winmm.dll; skip device enumeration on
            // non-Windows platforms so the reflection-based driver loader doesn't see a
            // DllNotFoundException at startup.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            devices = new List<Audio.Device>() { new Device() };
        }

        public override string Name
        {
            get { return "Windows Audio"; }
        }
    }
}
