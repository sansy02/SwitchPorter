namespace SwitchAlbum.Models;

public sealed class AppSettings
{
    /// <summary>保存到电脑的目录，默认 桌面\SwitchAlbum。</summary>
    public string SavePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SwitchAlbum");

    /// <summary>保存到手机的目标设备（MTP 设备 id）。</summary>
    public string? PhoneDeviceId { get; set; }
    public string? PhoneFriendlyName { get; set; }

    /// <summary>保存时按 日期_游戏名_序号 重命名。</summary>
    public bool AutoRename { get; set; } = true;

    /// <summary>照片网格的预览大小档位（0 最小，4 最大）。</summary>
    public int PhotoZoomLevel { get; set; } = 1;

    /// <summary>System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    // 窗口状态
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
}
