using System.Runtime.InteropServices;

namespace CoreAudio
{
    /// <summary>
    /// macOS audio driver — discovered by reflection from <see cref="Audio.Driver.Drivers"/>.
    ///
    /// Wraps miniaudio's CoreAudio backend rather than calling AudioUnit / AudioObject
    /// directly. See the upgrade plan's Tier 3 for rationale.
    /// </summary>
    public class Driver : Audio.Driver
    {
        public override string Name => "CoreAudio";

        public Driver()
        {
            if (!IsSupported())
                return;

            // TODO: enumerate devices via ma_context_get_devices and populate `devices`.
            // Skipped while the native miniaudio binary is not yet bundled — see Tier 3
            // open question (bundle precompiled vs. build from source in CI).
        }

        internal static bool IsSupported() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }
}
