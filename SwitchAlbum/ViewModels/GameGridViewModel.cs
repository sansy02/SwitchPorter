using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;

namespace SwitchAlbum.ViewModels;

public enum GridSortMode
{
    NewestFirst,
    OldestFirst,
}

/// <summary>单个游戏（或全部）的照片网格。行分块 + ListBox 虚拟化，支持按拍摄时间排序和调整预览大小。Switch 与电脑来源共用。</summary>
public partial class GameGridViewModel : ObservableObject
{
    /// <summary>视图量出真实宽度之前用的默认值（窗口默认宽度下的可用宽度）。</summary>
    private const double DefaultViewportWidth = 1200;

    private readonly MainViewModel _main;
    private readonly List<PhotoItemViewModel> _items;
    private double _viewportWidth = DefaultViewportWidth;

    public GameGridViewModel(string title, IReadOnlyList<AlbumItem> items, MainViewModel main)
    {
        Title = title;
        _main = main;
        _zoomLevel = PhotoGridLayout.ClampLevel(main.Settings.PhotoZoomLevel);
        _items = items.Select(i => new PhotoItemViewModel(i, main)).ToList();
        foreach (var item in _items)
        {
            item.SelectionChanged += OnItemSelectionChanged;
        }

        SortItems();
        ApplyLayout();
    }

    /// <summary>缩放或窗口变化导致行重排，参数是重排前视野里第一张照片的序号，视图据此滚回原位。</summary>
    public event Action<int>? RowsReflowed;

    public string Title { get; }
    public IReadOnlyList<PhotoItemViewModel> Items => _items;
    public ObservableCollection<PhotoRow> Rows { get; } = new();

    [ObservableProperty] private int _columns;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectedText = "";
    [ObservableProperty] private bool _canSaveSelected;
    [ObservableProperty] private GridSortMode _sortMode = GridSortMode.NewestFirst;
    [ObservableProperty] private int _zoomLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowHeight))]
    private double _cardWidth;

    public bool IsNewestFirst => SortMode == GridSortMode.NewestFirst;

    public double RowHeight => PhotoGridLayout.RowHeight(CardWidth);

    /// <summary>视野里第一张照片的序号，由视图在滚动时更新。</summary>
    public int FirstVisibleIndex { get; set; }

    partial void OnSortModeChanged(GridSortMode value) => OnPropertyChanged(nameof(IsNewestFirst));

    partial void OnZoomLevelChanged(int value)
    {
        _main.SavePhotoZoomLevel(value);
        ApplyLayout();
    }

    /// <summary>档位加减（Ctrl+滚轮）。</summary>
    public void ChangeZoom(int delta) => ZoomLevel = PhotoGridLayout.ClampLevel(ZoomLevel + delta);

    public void SetViewportWidth(double width)
    {
        _viewportWidth = width;
        ApplyLayout();
    }

    /// <summary>视野停稳后由视图调用：卡片够大时给看得见的照片加载原图，其余的取消排队。</summary>
    public void UpdateHighRes(int firstIndex, int count)
    {
        var wanted = CardWidth >= PhotoGridLayout.HighResFromWidth;
        for (var i = 0; i < _items.Count; i++)
        {
            if (wanted && i >= firstIndex && i < firstIndex + count)
            {
                _ = _items[i].LoadHighResAsync();
            }
            else
            {
                _items[i].CancelHighRes();
            }
        }
    }

    private void ApplyLayout()
    {
        var (columns, cardWidth) = PhotoGridLayout.Compute(_viewportWidth, ZoomLevel);
        CardWidth = cardWidth;
        if (cardWidth < PhotoGridLayout.HighResFromWidth)
        {
            // 缩小回去后缩略图已经够用，不再占着原图的内存
            foreach (var item in _items)
            {
                item.ReleaseHighRes();
            }
        }

        if (columns == Columns)
        {
            return;
        }

        var anchor = FirstVisibleIndex;
        Columns = columns;
        RebuildRows();
        RowsReflowed?.Invoke(anchor);
    }

    [RelayCommand]
    private void SortNewestFirst()
    {
        if (SortMode != GridSortMode.NewestFirst)
        {
            SortMode = GridSortMode.NewestFirst;
            SortItems();
            RebuildRows();
        }
    }

    [RelayCommand]
    private void SortOldestFirst()
    {
        if (SortMode != GridSortMode.OldestFirst)
        {
            SortMode = GridSortMode.OldestFirst;
            SortItems();
            RebuildRows();
        }
    }

    /// <summary>按拍摄时间排序；未知时间戳恒排最后。实例顺序变化不影响选中状态。</summary>
    private void SortItems()
    {
        _items.Sort((a, b) =>
        {
            var ta = a.Item.Timestamp == DateTime.MinValue ? DateTime.MaxValue : a.Item.Timestamp;
            var tb = b.Item.Timestamp == DateTime.MinValue ? DateTime.MaxValue : b.Item.Timestamp;
            var cmp = ta.CompareTo(tb);
            return SortMode == GridSortMode.NewestFirst ? -cmp : cmp;
        });
    }

    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var chunk in _items.Chunk(Columns))
        {
            Rows.Add(new PhotoRow(chunk));
        }
    }

    private void OnItemSelectionChanged(object? sender, bool selected)
    {
        SelectedCount = Math.Max(0, SelectedCount + (selected ? 1 : -1));
        SelectedText = SelectedCount > 0 ? string.Format(Strings.Lbl_Selected, SelectedCount) : "";
        CanSaveSelected = SelectedCount > 0;
        _main.OnGridSelectionChanged(this);
    }

    /// <summary>只勾选满足条件的照片（其余取消勾选）。</summary>
    public void SelectWhere(Func<AlbumItem, bool> predicate)
    {
        foreach (var item in _items)
        {
            item.IsSelected = predicate(item.Item);
        }
    }

    public IReadOnlyList<AlbumItem> SelectedItems()
        => _items.Where(i => i.IsSelected).Select(i => i.Item).ToList();

    public void SetAllSelected(bool selected)
    {
        foreach (var item in _items)
        {
            item.IsSelected = selected;
        }
    }

    public void RefreshSaved()
    {
        foreach (var item in _items)
        {
            item.RefreshSaved();
        }
    }

    public void OpenLightbox(PhotoItemViewModel item) => _main.OpenLightbox(_items, item);

    public void OpenVideo(PhotoItemViewModel item) => _ = _main.OpenVideoAsync(item);
}

public sealed class PhotoRow
{
    public PhotoRow(IReadOnlyList<PhotoItemViewModel> items) => Items = items;

    public IReadOnlyList<PhotoItemViewModel> Items { get; }
}
