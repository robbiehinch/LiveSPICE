using Avalonia;

namespace LiveSPICE.Avalonia
{
    internal static class Program
    {
        /// <summary>
        /// Optional .schx path supplied as the first command-line argument. The App reads
        /// this after framework init to load the schematic into the main window.
        /// </summary>
        public static string InitialSchematicPath { get; private set; }

        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                InitialSchematicPath = args[0];
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                // Make the app behave like a regular macOS GUI app even when launched
                // outside of an .app bundle (the default policy can hide the window
                // behind other apps and suppress the Dock icon).
                .With(new MacOSPlatformOptions { ShowInDock = true })
                .LogToTrace();
    }
}
