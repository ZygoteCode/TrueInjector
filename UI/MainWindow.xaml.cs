using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TrueInjector.Models;
using TrueInjector.UI.Pages;

namespace TrueInjector.UI;

public sealed partial class MainWindow : Window
{
    private sealed record NavItem(string Key, string Label, Geometry Icon);

    private readonly Dictionary<string, UserControl> _pages = [];
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.ToastRequested += request => Dispatcher.BeginInvoke(() => ShowToast(request));
        _viewModel.ShutdownRequested += () => Application.Current.Shutdown();

        NavList.ItemsSource = new[]
        {
            new NavItem("inject", "Inject", (Geometry)FindResource("IcTarget")),
            new NavItem("techniques", "Techniques", (Geometry)FindResource("IcSliders")),
            new NavItem("watchdog", "Watchdog", (Geometry)FindResource("IcPulse")),
            new NavItem("activity", "Activity", (Geometry)FindResource("IcTerminal")),
            new NavItem("guide", "Guide", (Geometry)FindResource("IcDownload")),
            new NavItem("about", "About", (Geometry)FindResource("IcInfo")),
        };

        Loaded += (_, _) =>
        {
            _pages.Clear();
            NavigateTo("inject");
            PositionNavPill(animated: false);
        };

        NavList.SizeChanged += (_, _) => PositionNavPill(animated: true);

        SourceInitialized += (_, _) => RoundCorners();
        StateChanged += (_, _) => CaptionShape.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "IcRestore" : "IcMaximize");
    }

    private void NavigateTo(string key)
    {
        UserControl? incoming = key switch
        {
            "inject" => _pages.TryGetValue(key, out UserControl? p1) ? p1 : _pages[key] = new InjectPage(),
            "techniques" => _pages.TryGetValue(key, out UserControl? p2) ? p2 : _pages[key] = new TechniquesPage(),
            "watchdog" => _pages.TryGetValue(key, out UserControl? p3) ? p3 : _pages[key] = new WatchdogPage(),
            "activity" => _pages.TryGetValue(key, out UserControl? p4) ? p4 : _pages[key] = new ActivityPage(),
            "guide" => _pages.TryGetValue(key, out UserControl? pX) ? pX : _pages[key] = new GuidePage(),
            "about" => _pages.TryGetValue(key, out UserControl? p5) ? p5 : _pages[key] = new AboutPage(),
            _ => null,
        };

        if (incoming is null || ReferenceEquals(PageHost.Content, incoming))
        {
            return;
        }

        if (PageHost.Content is not UserControl current)
        {
            SwapTo(incoming);
            return;
        }

        // Slide the outgoing page down gently, then bring the new one in.
        TimeSpan outro = TimeSpan.FromMilliseconds(80);
        DoubleAnimation fadeOut = new(1, 0, outro) { EasingFunction = new QuadraticEase() };
        fadeOut.Completed += (_, _) => SwapTo(incoming);
        current.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void SwapTo(UserControl incoming)
    {
        PageHost.Content = incoming;
        incoming.RenderTransform = new TranslateTransform(14, 0);
        incoming.Opacity = 0;

        TimeSpan intro = TimeSpan.FromMilliseconds(210);
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };

        DoubleAnimation fadeIn = new(0, 1, intro) { EasingFunction = ease };
        DoubleAnimation slide = new(14, 0, intro) { EasingFunction = ease };

        incoming.BeginAnimation(OpacityProperty, fadeIn);
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void NavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        PositionNavPill(animated: true);
        NavigateTo(item.Key);
    }

    private void PositionNavPill(bool animated)
    {
        if (NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        if (NavList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
        {
            return;
        }

        Point at = container.TranslatePoint(new Point(0, 0), NavList);
        Thickness target = new(0, at.Y, 0, 0);

        if (!animated)
        {
            NavPill.Margin = target;
            return;
        }

        ThicknessAnimation slide = new(NavPill.Margin, target, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        NavPill.BeginAnimation(MarginProperty, slide);
    }

    private void ShowToast(ToastRequest request)
    {
        Brush tint = request.Kind switch
        {
            ActivityKind.Success => (Brush)FindResource("B.Green"),
            ActivityKind.Error => (Brush)FindResource("B.Red"),
            _ => (Brush)FindResource("B.Accent"),
        };

        Geometry glyph = request.Kind switch
        {
            ActivityKind.Success => (Geometry)FindResource("IcCheck"),
            ActivityKind.Error => (Geometry)FindResource("IcFail"),
            _ => (Geometry)FindResource("IcInfo"),
        };

        Border card = new()
        {
            Background = FindResource("B.Elevated") as Brush,
            BorderBrush = FindResource("B.Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11, 14, 12),
            Effect = FindResource("E.Toast") as Effect,
            Opacity = 0,
        };

        StackPanel content = new() { Orientation = Orientation.Horizontal };
        System.Windows.Shapes.Path icon = new()
        {
            Data = glyph,
            Stroke = tint,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(icon);

        StackPanel text = new() { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = request.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            Foreground = FindResource("B.TextPrimary") as Brush,
        });
        text.Children.Add(new TextBlock
        {
            Text = request.Message,
            FontSize = 11.5,
            TextWrapping = TextWrap(),
            Foreground = FindResource("B.TextSecondary") as Brush,
            Margin = new Thickness(0, 2, 0, 0),
        });
        content.Children.Add(text);
        card.Child = content;

        ToastHost.Children.Insert(0, card);

        TranslateTransform shift = new(12, 0);
        card.RenderTransform = shift;

        TimeSpan easeIn = TimeSpan.FromMilliseconds(220);
        CubicEase ease = new() { EasingMode = EasingMode.EaseOut };

        DoubleAnimation appear = new(0, 1, easeIn) { EasingFunction = ease };
        DoubleAnimation rise = new(12, 0, easeIn) { EasingFunction = ease };

        card.BeginAnimation(OpacityProperty, appear);
        shift.BeginAnimation(TranslateTransform.XProperty, rise);

        DispatcherTimer dismiss = new() { Interval = TimeSpan.FromSeconds(4.2) };
        dismiss.Tick += (_, _) =>
        {
            dismiss.Stop();
            TimeSpan fadeTime = TimeSpan.FromMilliseconds(180);
            DoubleAnimation fade = new(1, 0, fadeTime) { EasingFunction = new QuadraticEase() };

            fade.Completed += (_, _) =>
            {
                ToastHost.Children.Remove(card);
            };

            card.BeginAnimation(OpacityProperty, fade);
        };
        dismiss.Start();
    }

    private static TextWrapping TextWrap() => TextWrapping.Wrap;

    private void RoundCorners()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch
        {
            // Pre-Windows-11 systems keep square corners, by design.
        }
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

internal static class DispatcherExtensions
{
    public static void DelayInvoke(this Dispatcher dispatcher, TimeSpan delay, Action action)
    {
        DispatcherTimer timer = new() { Interval = delay };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };

        timer.Start();
    }
}
