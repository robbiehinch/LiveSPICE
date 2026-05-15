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
                .LogToTrace();
    }
}
