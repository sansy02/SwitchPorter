using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwitchAlbum.Controls;

/// <summary>
/// ItemsControl 容器入场错峰动画：逐项延迟 32ms 淡入 + 上浮 + 微缩放（用于游戏卡片墙）。
/// </summary>
public static class StaggerBehavior
{
    private static readonly DependencyProperty AnimatedProperty = DependencyProperty.RegisterAttached(
        "Animated", typeof(bool), typeof(StaggerBehavior), new PropertyMetadata(false));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(StaggerBehavior), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl control)
        {
            return;
        }

        control.ItemContainerGenerator.StatusChanged -= OnStatusChanged;
        if ((bool)e.NewValue)
        {
            control.ItemContainerGenerator.StatusChanged += OnStatusChanged;
        }
    }

    private static void OnStatusChanged(object? sender, EventArgs e)
    {
        if (sender is not ItemContainerGenerator generator
            || generator.Status != GeneratorStatus.ContainersGenerated)
        {
            return;
        }

        for (var i = 0; i < generator.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is not FrameworkElement container
                || (bool)container.GetValue(AnimatedProperty))
            {
                continue;
            }

            container.SetValue(AnimatedProperty, true);
            AnimateIn(container, i);
        }
    }

    private static void AnimateIn(FrameworkElement element, int index)
    {
        // 卡片从下方轻轻浮起、由小到正常——像从雾里浮现，而不是弹出来
        element.Opacity = 0;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var translate = new TranslateTransform { Y = 22 };
        var scale = new ScaleTransform(0.96, 0.96);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);
        element.RenderTransform = group;

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 18) * 32);
        var duration = TimeSpan.FromMilliseconds(520);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, duration) { BeginTime = delay, EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, duration) { BeginTime = delay, EasingFunction = ease });
    }
}
