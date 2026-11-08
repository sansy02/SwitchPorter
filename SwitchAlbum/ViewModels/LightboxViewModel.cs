using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SwitchAlbum.ViewModels;

/// <summary>大图预览灯箱：左右切换、全图异步加载、直接勾选（看清内容再决定要不要这张）、视频调用系统播放器。</summary>
public sealed partial class LightboxViewModel : ObservableObject
{
    private readonly List<PhotoItemViewModel> _items;
    private readonly Func<PhotoItemViewModel, CancellationToken, Task<string?>> _fullImageLoader;
    private readonly Action<PhotoItemViewModel> _openVideo;
    private CancellationTokenSource? _cts;

    public LightboxViewModel(
        IReadOnlyList<PhotoItemViewModel> items,
        PhotoItemViewModel current,
        Func<PhotoItemViewModel, CancellationToken, Task<string?>> fullImageLoader,
        Action<PhotoItemViewModel> openVideo)
    {
        _items = items as List<PhotoItemViewModel> ?? items.ToList();
        _fullImageLoader = fullImageLoader;
        _openVideo = openVideo;
        SetCurrent(current);
    }

    public event Action? Closed;

    [ObservableProperty] private PhotoItemViewModel? _current;
    [ObservableProperty] private string? _fullPath;
    [ObservableProperty] private bool _isImageLoading;
    [ObservableProperty] private string _indexText = "";
    [ObservableProperty] private string _captionText = "";
    [ObservableProperty] private bool _canPrev;
    [ObservableProperty] private bool _canNext;

    public void Cancel() => _cts?.Cancel();

    private async void SetCurrent(PhotoItemViewModel item)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var index = _items.IndexOf(item);
        Current = item;
        IndexText = $"{index + 1} / {_items.Count}";
        CaptionText = item.FileName;
        CanPrev = index > 0;
        CanNext = index >= 0 && index < _items.Count - 1;
        FullPath = null;

        if (item.IsVideo)
        {
            IsImageLoading = false;
            return;
        }

        IsImageLoading = true;
        string? path;
        try
        {
            path = await _fullImageLoader(item, ct);
        }
        catch (OperationCanceledException)
        {
            return; // 已经翻到下一张，这张的结果不要了
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        FullPath = path;
        IsImageLoading = false;
    }

    [RelayCommand]
    private void ToggleSelected()
    {
        if (Current != null)
        {
            Current.IsSelected = !Current.IsSelected;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (Current == null)
        {
            return;
        }

        var index = _items.IndexOf(Current);
        if (index >= 0 && index < _items.Count - 1)
        {
            SetCurrent(_items[index + 1]);
        }
    }

    [RelayCommand]
    private void Prev()
    {
        if (Current == null)
        {
            return;
        }

        var index = _items.IndexOf(Current);
        if (index > 0)
        {
            SetCurrent(_items[index - 1]);
        }
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke();

    [RelayCommand]
    private void OpenVideo()
    {
        if (Current != null)
        {
            _openVideo(Current);
        }
    }
}
