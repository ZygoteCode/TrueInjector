using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TrueInjector.Models;

namespace TrueInjector.UI.Pages;

public sealed partial class TechniquesPage : UserControl
{
    public TechniquesPage()
    {
        InitializeComponent();
    }

    private void MethodChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: InjectionMethod method })
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Settings.Method = method;
            }
        }
    }
}
