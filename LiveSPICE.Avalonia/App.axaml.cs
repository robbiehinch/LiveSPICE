using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LiveSPICE.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainWindow window = new MainWindow();
                desktop.MainWindow = window;
                if (!string.IsNullOrEmpty(Program.InitialSchematicPath))
                    window.TryLoadSchematic(Program.InitialSchematicPath);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
