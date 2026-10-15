namespace SwitchAlbum.Models;

/// <summary>
/// 已保存记录（用于重复检测）。
/// Id：Switch 原始文件名（全局唯一，如 2025031118460900-02CB…4B947D.jpg）；
/// 非 Switch 命名的文件退化为 游戏名 + "|" + 文件名。
/// </summary>
public sealed class SavedRecord
{
    public string Id { get; set; } = "";
    public string GameTitle { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? RenamedTo { get; set; }
    public bool SavedToPc { get; set; }
    public bool SavedToPhone { get; set; }
    public DateTime SavedAt { get; set; }

    /// <summary>保存到电脑后的完整路径。判断「已保存」时会检查它是否仍然存在。</summary>
    public string? PcPath { get; set; }

    /// <summary>已保存到的手机：手机设备 id → 手机上的文件名。换手机后不会被误判为已保存。</summary>
    public Dictionary<string, string> PhoneTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
