using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SwitchAlbum.Controls;

/// <summary>空状态场景：显示时淡入并开始缓慢的环境动画，隐藏时停止（不在后台空耗）。</summary>
public partial class EmptyScene : UserControl
{
    private readonly Storyboard _ambient;
    private bool _running;

    public EmptyScene()
    {
        InitializeComponent();
        _ambient = (Storyboard)Resources["Ambient"];
        IsVisibleChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => Stop();
    }

    private void Sync()
    {
        if (IsVisible && IsLoaded)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _ambient.Begin(this, isControllable: true);
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(1200))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _ambient.Stop(this);
    }
}
