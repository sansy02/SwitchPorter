using CommunityToolkit.Mvvm.ComponentModel;
using SwitchAlbum.Models;

namespace SwitchAlbum.ViewModels;

public sealed partial class PhotoItemViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private int _thumbLoadStarted;

    public PhotoItemViewModel(AlbumItem item, MainViewModel main)
    {
        Item = item;
        _main = main;
        _savedState = item.Saved;
        _savedVisible = item.Saved != SavedState.NotSaved;
    }

    public AlbumItem Item { get; }
    public string DateText => Item.Timestamp == DateTime.MinValue
        ? "未知日期"
        : Item.Timestamp.ToString("yyyy-MM-dd HH:mm");
    public string FileName => Item.FileName;
    public bool IsVideo => Item.IsVideo;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _thumbPath;
    [ObservableProperty] private SavedState _savedState;
    [ObservableProperty] private bool _savedVisible;

    public event EventHandler<bool>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, value);

    /// <summary>「已存电脑 / 已存手机 / 电脑+手机」徽标文字。</summary>
    public string SavedText => SavedState switch
    {
        SavedState.SavedToPc => Resources.Strings.Badge_OnPc,
        SavedState.SavedToPhone => Resources.Strings.Badge_OnPhone,
        SavedState.SavedBoth => Resources.Strings.Badge_OnBoth,
        _ => "",
    };

    partial void OnSavedStateChanged(SavedState value) => OnPropertyChanged(nameof(SavedText));

    public void RefreshSaved()
    {
        SavedState = Item.Saved;
        SavedVisible = Item.Saved != SavedState.NotSaved;
    }

    /// <summary>由 PhotoCard.Loaded 触发；重复触发与并发加载有守卫。</summary>
    public async Task LoadThumbAsync()
    {
        if (Interlocked.Increment(ref _thumbLoadStarted) != 1)
        {
            return;
        }

        var path = await _main.LoadThumbnailAsync(this);
        if (path != null)
        {
            ThumbPath = path;
        }
    }
}
