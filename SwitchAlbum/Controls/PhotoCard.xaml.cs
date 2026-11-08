using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Controls;

public partial class PhotoCard : UserControl
{
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(PhotoCard),
        new PropertyMetadata(PhotoGridLayout.ZoomWidths[PhotoGridLayout.DefaultZoomLevel], (d, _) => ((PhotoCard)d).ApplyCardWidth()));

    private readonly CardMotion _motion;

    public PhotoCard()
    {
        InitializeComponent();
        _motion = new CardMotion(Root, RootScale, hoverScale: 1.02, liftBy: 3);
        ApplyCardWidth();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PhotoItemViewModel viewModel)
            {
                _ = viewModel.LoadThumbAsync();
            }
        };
        Loaded += (_, _) =>
        {
            if (DataContext is PhotoItemViewModel viewModel)
            {
                _ = viewModel.LoadThumbAsync();
            }
        };
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    private void ApplyCardWidth()
    {
        Width = CardWidth;
        Height = PhotoGridLayout.CardHeight(CardWidth);
        ImageRow.Height = new GridLength(PhotoGridLayout.ImageHeight(CardWidth));
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e) => _motion.Enter();

    private void Root_MouseLeave(object sender, MouseEventArgs e) => _motion.Leave();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not PhotoItemViewModel viewModel || SelectBox.IsMouseOver)
        {
            return;
        }

        var window = Window.GetWindow(this);
        if (window?.DataContext is MainViewModel main)
        {
            main.OpenPhoto(viewModel);
            e.Handled = true;
        }
    }
}
