using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveSPICE.Avalonia.Mcp;
using LiveSPICE.Avalonia.Services;
using LiveSPICE.Avalonia.Windows;

namespace LiveSPICE.Avalonia
{
    public partial class App : Application
    {
        private McpServer mcpServer;

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

                StartMcpServerIfEnabled(window);
                desktop.Exit += (_, _) => mcpServer?.Stop();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private void StartMcpServerIfEnabled(MainWindow window)
        {
            Settings settings = Settings.Load();
            if (!settings.McpEnabled) return;
            try
            {
                McpHost host = new McpHost(window);
                McpToolRegistry registry = new McpToolRegistry();
                LiveSpiceTools.Register(registry, host);
                mcpServer = new McpServer(registry, settings.McpPort, msg => Debug.WriteLine(msg));
                mcpServer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MCP server start failed: " + ex);
            }
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
