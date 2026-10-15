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

/// <summary>单个游戏（或全部）的照片网格。行分块 + ListBox 虚拟化，支持按拍摄时间排序。Switch 与电脑来源共用。</summary>
public partial class GameGridViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly List<PhotoItemViewModel> _items;

    public GameGridViewModel(string title, IReadOnlyList<AlbumItem> items, MainViewModel main)
    {
        Title = title;
        _main = main;
        _items = items.Select(i => new PhotoItemViewModel(i, main)).ToList();
        foreach (var item in _items)
        {
            item.SelectionChanged += OnItemSelectionChanged;
        }

        SortItems();
        SetColumns(4);
    }

    public string Title { get; }
    public IReadOnlyList<PhotoItemViewModel> Items => _items;
    public ObservableCollection<PhotoRow> Rows { get; } = new();

    [ObservableProperty] private int _columns;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectedText = "";
    [ObservableProperty] private bool _canSaveSelected;
    [ObservableProperty] private GridSortMode _sortMode = GridSortMode.NewestFirst;

    public bool IsNewestFirst => SortMode == GridSortMode.NewestFirst;

    partial void OnSortModeChanged(GridSortMode value) => OnPropertyChanged(nameof(IsNewestFirst));

    public void SetColumns(int columns)
    {
        if (columns < 2)
        {
            columns = 2;
        }

        if (columns == Columns)
        {
            return;
        }

        Columns = columns;
        RebuildRows();
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
