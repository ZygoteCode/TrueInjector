using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace TrueInjector.UI.Pages;

public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void OpenRepository(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/ZygoteCode/TrueInjector/") { UseShellExecute = true });
        }
        catch
        {
            // Browser launch is best-effort.
        }
    }
}
