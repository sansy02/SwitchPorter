using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SwitchAlbum.Controls;

/// <summary>
/// 卡片悬停动效：轻微放大 + 上浮 + 一圈很淡的紫色光晕。
/// 光晕只在悬停时挂上、移开后撤掉——几百张卡片同时带阴影会拖慢滚动。
/// </summary>
internal sealed class CardMotion
{
    private static readonly Duration EnterDuration = TimeSpan.FromMilliseconds(260);
    private static readonly Duration LeaveDuration = TimeSpan.FromMilliseconds(320);
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly FrameworkElement _target;
    private readonly ScaleTransform _scale;
    private readonly TranslateTransform _lift = new();
    private readonly double _hoverScale;
    private readonly double _liftBy;
    private DropShadowEffect? _glow;

    public CardMotion(FrameworkElement target, ScaleTransform scale, double hoverScale, double liftBy)
    {
        _target = target;
        _scale = scale;
        _hoverScale = hoverScale;
        _liftBy = liftBy;

        // 把 XAML 里的 ScaleTransform 和上浮位移组合起来
        target.RenderTransform = null;
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(_lift);
        target.RenderTransform = group;
    }

    public void Enter()
    {
        Animate(_hoverScale, -_liftBy, EnterDuration);

        var color = Application.Current.TryFindResource("AccentGlowColor") is Color c ? c : Color.FromRgb(0x8C, 0x6C, 0xFF);
        _glow = new DropShadowEffect
        {
            Color = color,
            BlurRadius = 26,
            ShadowDepth = 6,
            Direction = 270,
            Opacity = 0,
            RenderingBias = RenderingBias.Performance,
        };
        _target.Effect = _glow;
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.32, EnterDuration) { EasingFunction = Ease });
    }

    public void Leave()
    {
        Animate(1.0, 0, LeaveDuration);

        var glow = _glow;
        if (glow == null)
        {
            return;
        }

        var fade = new DoubleAnimation(0, LeaveDuration) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_target.Effect, glow) && !_target.IsMouseOver)
            {
                _target.Effect = null;
            }
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    private void Animate(double scale, double y, Duration duration)
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = Ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = Ease });
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = Ease });
    }
}
