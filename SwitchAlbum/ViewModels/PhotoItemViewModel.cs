using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchAlbum.Models;

namespace SwitchAlbum.ViewModels;

public sealed partial class PhotoItemViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private int _thumbLoadStarted;
    private CancellationTokenSource? _highResCts;

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
    public bool CanSetAsCover => !IsVideo;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPath))]
    private string? _thumbPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPath))]
    private string? _highResPath;

    [ObservableProperty] private SavedState _savedState;
    [ObservableProperty] private bool _savedVisible;

    /// <summary>卡片上显示的图：原图到了用原图，否则先用缩略图。</summary>
    public string? DisplayPath => HighResPath ?? ThumbPath;

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

    /// <summary>由网格在照片可见时触发；离开视野时用 <see cref="CancelHighRes"/> 取消排队中的下载。</summary>
    public async Task LoadHighResAsync()
    {
        if (IsVideo || HighResPath != null || _highResCts != null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _highResCts = cts;
        try
        {
            var path = await _main.LoadHighResAsync(this, cts.Token);
            if (path != null && !cts.IsCancellationRequested)
            {
                HighResPath = path;
            }
        }
        catch (OperationCanceledException)
        {
            // 滚出视野了，下次看到时再加载
        }
        finally
        {
            if (ReferenceEquals(_highResCts, cts))
            {
                _highResCts = null;
            }

            cts.Dispose();
        }
    }

    public void CancelHighRes()
    {
        _highResCts?.Cancel();
        _highResCts = null; // 立刻清掉：紧接着可能再次请求，不能被当成「已在加载」
    }

    public void ReleaseHighRes()
    {
        CancelHighRes();
        HighResPath = null;
    }

    [RelayCommand(CanExecute = nameof(CanSetAsCover))]
    private Task SetAsCoverAsync() => _main.SetCoverFromPhotoAsync(this);
}
