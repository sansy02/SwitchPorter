namespace SwitchAlbum.ViewModels;

/// <summary>已废弃：Switch 与电脑来源现在共用 <see cref="GameGridViewModel"/>。此文件可以删除。</summary>
[Obsolete("使用 GameGridViewModel")]
public sealed class LocalGameGridViewModel : GameGridViewModel
{
    public LocalGameGridViewModel(string title, IReadOnlyList<Models.AlbumItem> items, MainViewModel main)
        : base(title, items, main)
    {
    }
}
