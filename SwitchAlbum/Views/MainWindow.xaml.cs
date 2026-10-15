using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.Services;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _main;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SwitchTab.SizeChanged += (_, _) => MoveTabIndicator(animate: false);
        LibraryTab.SizeChanged += (_, _) => MoveTabIndicator(animate: false);
    }

    /// <summary>紫色胶囊滑到当前来源标签下面。</summary>
    private void MoveTabIndicator(bool animate)
    {
        if (_main == null || !IsLoaded)
        {
            return;
        }

        var target = _main.IsLibrarySource ? LibraryTab : SwitchTab;
        if (target.ActualWidth <= 0)
        {
            return;
        }

        var x = target.TranslatePoint(new Point(0, 0), SwitchTab).X;
        if (!animate)
        {
            TabIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            TabIndicator.BeginAnimation(WidthProperty, null);
            TabIndicatorShift.X = x;
            TabIndicator.Width = target.ActualWidth;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(380);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 };
        TabIndicatorShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(x, duration) { EasingFunction = ease });
        TabIndicator.BeginAnimation(WidthProperty,
            new DoubleAnimation(target.ActualWidth, duration) { EasingFunction = ease });
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_main != null)
        {
            _main.PropertyChanged -= OnMainPropertyChanged;
        }

        _main = e.NewValue as MainViewModel;
        if (_main != null)
        {
            _main.PropertyChanged += OnMainPropertyChanged;
        }
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsLibrarySource))
        {
            MoveTabIndicator(animate: true);
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.Lightbox))
        {
            return;
        }

        if (_main?.Lightbox != null)
        {
            // 打开大图：从稍小处放大到位
            AnimateLightboxScale(0.94, 420);
        }

        // 灯箱打开/切换时订阅全图加载事件，做 crossfade
        if (_main?.Lightbox is { } lightbox)
        {
            lightbox.PropertyChanged -= OnLightboxPropertyChanged;
            lightbox.PropertyChanged += OnLightboxPropertyChanged;
        }
    }

    private void OnLightboxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LightboxViewModel.FullPath))
        {
            return;
        }

        AnimateLightboxScale(0.985, 300);
        LightboxImage.BeginAnimation(OpacityProperty, null);
        LightboxImage.Opacity = 0;
        LightboxImage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void AnimateLightboxScale(double from, int milliseconds)
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        LightboxScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
        LightboxScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() => MoveTabIndicator(animate: false), System.Windows.Threading.DispatcherPriority.Loaded);
        if (_main == null)
        {
            return;
        }

        var settings = _main.Settings;
        Width = Math.Min(settings.WindowWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Min(settings.WindowHeight, SystemParameters.VirtualScreenHeight);

        // 显示器布局可能已变化：保存的位置越出虚拟屏时不再恢复（交给 CenterScreen 居中），
        // 防止窗口跑到屏幕外——进程在后台、界面却看不见。
        if (WindowBounds.IsVisibleOnScreen(
                settings.WindowLeft, settings.WindowTop, Width, Height,
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        else if (!double.IsNaN(settings.WindowLeft) || !double.IsNaN(settings.WindowTop))
        {
            Log.Info($"保存的窗口位置越出当前屏幕（{settings.WindowLeft},{settings.WindowTop}），已重置为居中");
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _ = _main.StartAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_main == null)
        {
            return;
        }

        var bounds = RestoreBounds;
        _main.SaveWindowBounds(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
        _ = _main.ShutdownAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var lightbox = _main?.Lightbox;
        if (lightbox == null)
        {
            // Ctrl+A：在照片网格里全选（搜索框里输入时不拦截）
            if (_main != null && e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control
                && Keyboard.FocusedElement is not System.Windows.Controls.TextBox && _main.IsInGrid)
            {
                _main.SelectAllCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                lightbox.CloseCommand.Execute(null);
                break;
            case Key.Left:
                lightbox.PrevCommand.Execute(null);
                break;
            case Key.Right:
                lightbox.NextCommand.Execute(null);
                break;
        }
    }
}
