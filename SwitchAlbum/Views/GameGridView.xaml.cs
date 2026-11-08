using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Views;

public partial class GameGridView : UserControl
{
    /// <summary>
    /// 滚动停下这么久才给可见照片加载原图：快速滚动时划过的照片不产生下载。
    /// 不能让每张卡片在 Loaded/Unloaded 里自己决定：虚拟化是延迟清理的，滚走的卡片还会活一阵子。
    /// </summary>
    private static readonly TimeSpan HighResDelay = TimeSpan.FromMilliseconds(250);

    // 默认的 Background 优先级会连带压低回调里 await 之后的代码，界面一忙原图就迟迟显示不出来
    private readonly DispatcherTimer _highResTimer = new(DispatcherPriority.Normal) { Interval = HighResDelay };
    private GameGridViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;

    public GameGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _highResTimer.Tick += (_, _) =>
        {
            _highResTimer.Stop();
            RequestHighResForVisibleRows();
        };
        Unloaded += (_, _) =>
        {
            _highResTimer.Stop();
            _viewModel?.UpdateHighRes(0, 0);
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RowsReflowed -= OnRowsReflowed;
        }

        _viewModel = e.NewValue as GameGridViewModel;
        if (_viewModel != null)
        {
            _viewModel.RowsReflowed += OnRowsReflowed;
            UpdateViewportWidth();
        }
    }

    private void GridList_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateViewportWidth();

    private void UpdateViewportWidth()
    {
        if (_viewModel == null || GridList.ActualWidth <= 0)
        {
            return;
        }

        // 始终预留滚动条宽度：否则滚动条一出现，列宽变化又让滚动条消失，来回跳
        _viewModel.SetViewportWidth(
            GridList.ActualWidth - GridList.Padding.Left - GridList.Padding.Right - SystemParameters.VerticalScrollBarWidth);
    }

    private void GridList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _viewModel.ChangeZoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }

        // 按像素滚动时默认每格只走 48px，大卡片要滚很久：改成每格一行
        if (FindScrollViewer() is { } viewer)
        {
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 120.0 * _viewModel.RowHeight);
            e.Handled = true;
        }
    }

    private void GridList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        _viewModel.FirstVisibleIndex = (int)(e.VerticalOffset / _viewModel.RowHeight) * _viewModel.Columns;
        _highResTimer.Stop();
        _highResTimer.Start();
    }

    private void RequestHighResForVisibleRows()
    {
        if (_viewModel == null || FindScrollViewer() is not { } viewer)
        {
            return;
        }

        var firstRow = (int)(viewer.VerticalOffset / _viewModel.RowHeight);
        var rowCount = (int)Math.Ceiling(viewer.ViewportHeight / _viewModel.RowHeight) + 1;
        _viewModel.UpdateHighRes(firstRow * _viewModel.Columns, rowCount * _viewModel.Columns);
    }

    /// <summary>缩放后回到缩放前看的那张照片，不要跳回顶部。行重排后要等布局完成才能滚动。</summary>
    private void OnRowsReflowed(int firstVisibleIndex)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_viewModel != null)
            {
                FindScrollViewer()?.ScrollToVerticalOffset(firstVisibleIndex / _viewModel.Columns * _viewModel.RowHeight);
            }
        });
    }

    private ScrollViewer? FindScrollViewer()
    {
        if (_scrollViewer != null)
        {
            return _scrollViewer;
        }

        DependencyObject current = GridList;
        while (current is not ScrollViewer)
        {
            if (VisualTreeHelper.GetChildrenCount(current) == 0)
            {
                return null;
            }

            current = VisualTreeHelper.GetChild(current, 0);
        }

        return _scrollViewer = (ScrollViewer)current;
    }
}
