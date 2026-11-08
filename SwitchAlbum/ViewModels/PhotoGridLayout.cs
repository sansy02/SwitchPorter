namespace SwitchAlbum.ViewModels;

/// <summary>
/// 照片网格的尺寸计算（纯函数，便于测试）。用户选的是档位（目标卡片宽度），实际宽度按窗口铺满一行。
/// 图片区严格 16:9，和 Switch 截图同比例，不裁边，画面底部的对话文字不会被切掉。
/// </summary>
public static class PhotoGridLayout
{
    /// <summary>
    /// 各档的目标卡片宽度。最大档在默认窗口下是 2 列、约 600 宽：1280×720 的截图缩到约一半，文字冒险的对话能看清。
    /// 每一档在默认窗口下的列数都不同（6/5/4/3/2），拖动滑块总能看到变化。
    /// </summary>
    public static readonly double[] ZoomWidths = { 170, 212, 270, 360, 600 };

    public const int DefaultZoomLevel = 1;
    public const int MaxZoomLevel = 4;

    /// <summary>Slider.Maximum 是 double，在 XAML 里直接给 int 运行时会抛异常。</summary>
    public const double ZoomSliderMaximum = MaxZoomLevel;

    /// <summary>卡片左右外边距之和（PhotoCard Margin=4）。</summary>
    public const double CardGap = 8;

    /// <summary>卡片底部日期、文件名那一行的高度。</summary>
    public const double InfoHeight = 54;

    /// <summary>卡片宽到这个值就改用原图：Switch 自带的缩略图不够清晰，放大后依然看不清字。</summary>
    public const double HighResFromWidth = 320;

    private const int MinColumns = 2;

    public static int ClampLevel(int level) => Math.Clamp(level, 0, MaxZoomLevel);

    public static (int Columns, double CardWidth) Compute(double availableWidth, int zoomLevel)
    {
        var target = ZoomWidths[ClampLevel(zoomLevel)];
        var columns = Math.Max(MinColumns, (int)(availableWidth / (target + CardGap)));
        var width = Math.Floor(availableWidth / columns) - CardGap;
        return (columns, Math.Max(ZoomWidths[0], width));
    }

    public static double ImageHeight(double cardWidth) => Math.Round(cardWidth * 9 / 16);

    public static double CardHeight(double cardWidth) => ImageHeight(cardWidth) + InfoHeight;

    public static double RowHeight(double cardWidth) => CardHeight(cardWidth) + CardGap;
}
