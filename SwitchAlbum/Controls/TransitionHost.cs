using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Controls;

/// <summary>
/// 内容切换容器，按「空间关系」选择过渡：
/// 进入游戏（墙 → 网格）= 镜头向前推近；返回游戏墙 = 镜头拉远；其他切换 = 轻微上浮淡入。
/// 需配合 App 资源中的隐式样式（模板内含 PART_ContentHost）。
/// </summary>
public class TransitionHost : ContentControl
{
    private FrameworkElement? _presenter;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _presenter = Template.FindName("PART_ContentHost", this) as FrameworkElement;
        AnimateIn(null, Content);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        AnimateIn(oldContent, newContent);
    }

    private void AnimateIn(object? oldContent, object? newContent)
    {
        if (_presenter == null || newContent == null || PresentationSource.FromVisual(this) == null)
        {
            return;
        }

        double fromScale = 1, fromY = 14;
        if (newContent is GameGridViewModel && oldContent is GameWallViewModel)
        {
            fromScale = 0.965; // 推近
            fromY = 0;
        }
        else if (newContent is GameWallViewModel && oldContent is GameGridViewModel)
        {
            fromScale = 1.03; // 拉远
            fromY = 0;
        }

        _presenter.BeginAnimation(OpacityProperty, null);
        _presenter.RenderTransformOrigin = new Point(0.5, 0.35);
        var scale = new ScaleTransform(fromScale, fromScale);
        var translate = new TranslateTransform(0, fromY);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        _presenter.RenderTransform = group;
        _presenter.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(420);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        _presenter.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }
}
