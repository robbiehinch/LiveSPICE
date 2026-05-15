using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LiveSPICE.Avalonia.Windows
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            string ver = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            versionText.Text = "Version " + ver;
        }

        private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    }
}
