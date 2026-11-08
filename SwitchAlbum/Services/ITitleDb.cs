namespace SwitchAlbum.Services;

/// <summary>游戏名 ↔ TitleId 映射库（由打包的 titles.json 实现）。</summary>
public interface ITitleDb
{
    /// <summary>TitleId → 最佳中文名（无中文则英文）。</summary>
    string? GetNameByTitleId(string titleId);

    /// <summary>规范化游戏名 → TitleId（用于封面图源，按 titleId 取图）。</summary>
    string? GetTitleIdByName(string titleName);

    /// <summary>
    /// 相册文件夹名 → TitleId。文件夹名是本地化游戏名（可能带后缀/括号/日文等变体），
    /// 依次尝试：规范化精确匹配 → 繁转简 → 剥离后缀/括号 → 前缀匹配。
    /// </summary>
    string? GetTitleIdByFolderName(string folderName);

    /// <summary>TitleId → 任天堂 eShop 官方封面直链（按 图标→横幅→盒装 顺序，可能为空）。</summary>
    string? GetIconUrl(string titleId);

    /// <summary>TitleId → 封面候选直链列表（按优先级排序，可能为空）。</summary>
    IReadOnlyList<string> GetCoverUrls(string titleId);
}
