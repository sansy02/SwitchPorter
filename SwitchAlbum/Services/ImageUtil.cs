using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwitchAlbum.Services;

/// <summary>图片工具（仅 Windows，用于缩略图降采样与演示数据生成）。</summary>
internal static class ImageUtil
{
    public static Image Downscale(Image source, int maxDimension)
    {
        var scale = Math.Min(1.0, (double)maxDimension / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));

        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }

    /// <summary>
    /// 把任意图片存成封面 PNG。用 WIC 解码（比 GDI+ 多支持 WebP 等网上常见格式）；
    /// 先写临时文件再改名，图片打不开时不会留下半截文件。
    /// </summary>
    public static void SaveAsCover(string sourcePath, string destPath, int maxSide)
    {
        BitmapSource frame;
        using (var input = File.OpenRead(sourcePath))
        {
            frame = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
        }

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
        if (scale < 1.0)
        {
            frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));

        var tmp = destPath + ".tmp";
        try
        {
            using (var output = File.Create(tmp))
            {
                encoder.Save(output);
            }

            File.Move(tmp, destPath, overwrite: true);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
