using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Controls;

public partial class GameCard : UserControl
{
    private readonly CardMotion _motion;

    public GameCard()
    {
        InitializeComponent();
        _motion = new CardMotion(Root, RootScale, hoverScale: 1.035, liftBy: 5);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameCardViewModel viewModel)
            {
                _ = viewModel.EnsureCoverAsync();
            }
        };
        Loaded += (_, _) =>
        {
            if (DataContext is GameCardViewModel viewModel)
            {
                _ = viewModel.EnsureCoverAsync();
            }
        };
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e) => _motion.Enter();

    private void Root_MouseLeave(object sender, MouseEventArgs e) => _motion.Leave();

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GameCardViewModel viewModel)
        {
            viewModel.OpenCommand.Execute(null);
        }
    }
}
