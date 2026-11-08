using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

/// <summary>游戏卡片墙（Switch 相册风格：封面 + 游戏名 + 数量，首卡为「全部」），支持按名称搜索。</summary>
public sealed partial class GameWallViewModel : ObservableObject
{
    private readonly List<GameCardViewModel> _allCards = new();

    public GameWallViewModel(ScanResult scan, MainViewModel main)
    {
        _allCards.Add(new GameCardViewModel(
            Strings.Card_AllGames, devicePath: null, items: scan.AllItems,
            isAllCard: true, titleId: null, main));

        foreach (var game in scan.Games)
        {
            var items = scan.ItemsByGame.TryGetValue(game.DevicePath, out var list)
                ? list
                : Array.Empty<Models.AlbumItem>();
            _allCards.Add(new GameCardViewModel(game.Title, game.DevicePath, items, isAllCard: false, game.TitleId, main));
        }

        ApplyFilter();
    }

    public ObservableCollection<GameCardViewModel> Cards { get; } = new();

    /// <summary>所有卡片，不受搜索过滤影响。</summary>
    public IReadOnlyList<GameCardViewModel> AllCards => _allCards;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _noResults;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Cards.Clear();
        var query = SearchText.Trim();

        // 搜索时隐藏「全部」卡片，仅按名称过滤游戏
        if (query.Length == 0)
        {
            foreach (var card in _allCards)
            {
                Cards.Add(card);
            }
        }
        else
        {
            foreach (var card in _allCards.Where(c => !c.IsAllCard
                         && c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                Cards.Add(card);
            }
        }

        NoResults = Cards.Count == 0;
    }
}
