using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveSPICE.Avalonia.Windows;

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
            HookGlobalExceptionHandlers();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainWindow window = new MainWindow();
                desktop.MainWindow = window;
                if (!string.IsNullOrEmpty(Program.InitialSchematicPath))
                    window.TryLoadSchematic(Program.InitialSchematicPath);
            }
            base.OnFrameworkInitializationCompleted();
        }

        private static void HookGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    Dispatcher.UIThread.Post(() => UnhandledExceptionWindow.Show(ex));
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Exception ex = args.Exception;
                args.SetObserved();
                Dispatcher.UIThread.Post(() => UnhandledExceptionWindow.Show(ex));
            };

            Dispatcher.UIThread.UnhandledException += (_, args) =>
            {
                args.Handled = true;
                UnhandledExceptionWindow.Show(args.Exception);
            };
        }
    }
}
