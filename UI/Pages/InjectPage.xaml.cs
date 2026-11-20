using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TrueInjector.Models;

namespace TrueInjector.UI.Pages;

public sealed partial class InjectPage : UserControl
{
    private int _dragDepth;

    public InjectPage()
    {
        InitializeComponent();

        Drop += OnDrop;
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Loaded += (_, _) => SearchBox.Focus();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            _dragDepth++;
            Highlight(true);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (--_dragDepth <= 0)
        {
            _dragDepth = 0;
            Highlight(false);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragDepth = 0;
        Highlight(false);

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            ViewModel.DllPath = files[0];
            e.Handled = true;
        }
    }

    private void DropZoneClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.BrowseForDll();
    }

    private void Highlight(bool on)
    {
        DropZone.Background = on ? FindResource("B.AccentDim") as Brush : FindResource("B.Rail") as Brush;
        DropZone.BorderBrush = on ? FindResource("B.Accent") as Brush : FindResource("B.Stroke") as Brush;
    }
}
