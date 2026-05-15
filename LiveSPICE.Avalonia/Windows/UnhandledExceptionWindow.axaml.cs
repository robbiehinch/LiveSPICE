using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace LiveSPICE.Avalonia.Windows
{
    public partial class UnhandledExceptionWindow : Window
    {
        public UnhandledExceptionWindow() { InitializeComponent(); }

        public UnhandledExceptionWindow(Exception ex) : this()
        {
            detailText.Text = (ex?.ToString() ?? "(no exception details)");
        }

        public static void Show(Exception ex)
        {
            try
            {
                Window owner = null;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                    owner = d.MainWindow;
                UnhandledExceptionWindow w = new UnhandledExceptionWindow(ex);
                if (owner != null) w.ShowDialog(owner);
                else w.Show();
            }
            catch
            {
                // Bail silently — if we can't show the window, we'll at least not crash worse.
            }
        }

        private async void CopyClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                IClipboard cb = TopLevel.GetTopLevel(this)?.Clipboard;
                if (cb != null) await cb.SetTextAsync(detailText.Text ?? string.Empty);
            }
            catch { /* ignore */ }
        }

        private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    }
}
