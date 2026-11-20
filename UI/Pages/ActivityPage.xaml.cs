using System.Collections.Specialized;
using System.Windows.Controls;
using TrueInjector.Models;

namespace TrueInjector.UI.Pages;

public sealed partial class ActivityPage : UserControl
{
    public ActivityPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Activity.CollectionChanged += OnActivityChanged;
            }
        };
    }

    private void OnActivityChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && FeedList.Items.Count > 0)
        {
            FeedList.ScrollIntoView(FeedList.Items[^1]);
        }
    }
}
