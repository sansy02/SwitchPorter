using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;

namespace SwitchAlbum.ViewModels;

public sealed partial class GameCardViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IReadOnlyList<AlbumItem> _items;
    private int _coverLoadStarted;

    public GameCardViewModel(
        string title, string? devicePath, IReadOnlyList<AlbumItem> items,
        bool isAllCard, string? titleId, MainViewModel main)
    {
        _main = main;
        _items = items;
        Title = title;
        DevicePath = devicePath;
        IsAllCard = isAllCard;
        TitleId = titleId;
        _subtitle = ComputeSubtitle();
    }

    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isAllCard;
    [ObservableProperty] private string _subtitle;
    [ObservableProperty] private string? _coverPath;

    public string? TitleId { get; private set; }
    public string? DevicePath { get; }
    public int PhotoCount => _items.Count(i => !i.IsVideo);
    public int VideoCount => _items.Count(i => i.IsVideo);
    public int SavedCount => _items.Count(i => i.Saved != SavedState.NotSaved);

    [RelayCommand]
    private void Open() => _main.OpenGame(this);

    public void RefreshSaved()
    {
        Subtitle = ComputeSubtitle();
    }

    public async Task EnsureCoverAsync()
    {
        // TitleId 为 null 也尝试：至少留下失败日志，便于排查名称反查缺口
        if (Interlocked.Increment(ref _coverLoadStarted) != 1)
        {
            return;
        }

        var path = await _main.ResolveCoverAsync(TitleId, Title);
        if (path != null)
        {
            CoverPath = path;
        }
    }

    /// <summary>游戏库在线更新后（如补齐了 Switch 2 游戏），用新的 TitleId 重新取封面。</summary>
    public void RetryCover(string titleId)
    {
        if (CoverPath != null || titleId == TitleId)
        {
            return;
        }

        TitleId = titleId;
        Interlocked.Exchange(ref _coverLoadStarted, 0);
        _ = EnsureCoverAsync();
    }

    private string ComputeSubtitle()
    {
        var parts = new List<string>();
        if (PhotoCount > 0)
        {
            parts.Add(string.Format(Strings.Card_PhotoCount, PhotoCount));
        }

        if (VideoCount > 0)
        {
            parts.Add(string.Format(Strings.Card_VideoCount, VideoCount));
        }

        if (SavedCount > 0)
        {
            parts.Add(string.Format("{0} {1}", Strings.Badge_Saved, SavedCount));
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "";
    }
}
