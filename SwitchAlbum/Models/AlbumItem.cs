namespace SwitchAlbum.Models;

/// <summary>相册中的一张照片或一个视频。</summary>
public sealed class AlbumItem
{
    public required string GameTitle { get; init; }

    /// <summary>设备上的完整文件路径（下载源）。</summary>
    public required string DevicePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>从文件名解析出的拍摄时间；解析失败为 DateTime.MinValue。</summary>
    public DateTime Timestamp { get; init; }

    public bool IsVideo { get; init; }

    public long Size { get; init; }

    public DateTime? LastModified { get; init; }

    public SavedState Saved { get; set; }

    /// <summary>
    /// 去重记录键的覆盖值：本地相册页的文件已被重命名，这里指回它对应的 Switch 原图记录，
    /// 这样「电脑 → 手机」与「Switch 直传手机」共用同一条记录，不会产生两份副本。
    /// </summary>
    public string? TrackingKey { get; set; }
}
