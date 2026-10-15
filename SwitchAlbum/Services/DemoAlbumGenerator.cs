using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace SwitchAlbum.Services;

/// <summary>生成模拟相册演示数据：Album\游戏名\&lt;时间戳&gt;_s.jpg|.mp4，另含旧式 titleid 文件夹。</summary>
public static class DemoAlbumGenerator
{
    private static readonly (string Name, string TitleId)[] Games =
    {
        ("塞尔达传说 王国之泪", "0100F2C0115B6000"),
        ("马力欧卡丁车8 豪华版", "0100152000022000"),
        ("集合啦！动物森友会", "01006F8002326000"),
        ("斯普拉遁 3", "0100C2500FC20000"),
        ("超级马力欧 奥德赛", "0100000000010000"),
        ("宝可梦 朱", "0100A3D008C5C000"),
    };

    public static void Generate(string albumRoot)
    {
        Directory.CreateDirectory(albumRoot);
        var albumDir = Path.Combine(albumRoot, "Album");
        if (Directory.Exists(albumDir))
        {
            Directory.Delete(albumDir, recursive: true);
        }

        Directory.CreateDirectory(albumDir);

        var random = new Random(20260910);
        var now = DateTime.Now;

        for (var g = 0; g < Games.Length; g++)
        {
            var (name, _) = Games[g];
            var gameDir = Path.Combine(albumDir, name);
            Directory.CreateDirectory(gameDir);

            var photoCount = 4 + random.Next(4); // 4-7 张
            var videoCount = random.Next(2);     // 0-1 个视频
            for (var p = 0; p < photoCount + videoCount; p++)
            {
                var ts = now.AddDays(-random.Next(90)).AddHours(-random.Next(12)).AddMinutes(-random.Next(60));
                var baseName = ts.ToString("yyyyMMddHHmmss") + "00";
                if (p < photoCount)
                {
                    SaveJpeg(Path.Combine(gameDir, baseName + "_s.jpg"), name, ts, g);
                }
                else
                {
                    WriteFakeVideo(Path.Combine(gameDir, baseName + "_s.mp4"), ts);
                }
            }
        }

        // 旧式 <时间戳>-<titleid> 文件夹，用于验证反查逻辑
        var legacyDir = Path.Combine(albumDir, "2024081512304500-0100F2C0115B6000");
        Directory.CreateDirectory(legacyDir);
        SaveJpeg(Path.Combine(legacyDir, "2024081512304500.jpg"), "旧式文件夹", new DateTime(2024, 8, 15, 12, 30, 45), 0);

        // 含全角符号的游戏名，用于验证清理逻辑保留合法全角字符
        var weirdDir = Path.Combine(albumDir, "NARUTO×BORUTO 新忍出击");
        Directory.CreateDirectory(weirdDir);
        SaveJpeg(Path.Combine(weirdDir, "2024060108000000_s.jpg"), "NARUTO×BORUTO 新忍出击", new DateTime(2024, 6, 1, 8, 0, 0), 1);
    }

    private static void SaveJpeg(string path, string title, DateTime timestamp, int hue)
    {
        using var bitmap = new Bitmap(1280, 720);
        using (var g = Graphics.FromImage(bitmap))
        {
            var c1 = Hsv(hue * 60, 0.55, 0.35);
            var c2 = Hsv(hue * 60, 0.45, 0.14);
            using var brush = new LinearGradientBrush(new Rectangle(0, 0, 1280, 720), c1, c2, 30f);
            g.FillRectangle(brush, 0, 0, 1280, 720);

            // 十字参考线，模拟游戏画面
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 2);
            g.DrawLine(pen, 0, 360, 1280, 360);
            g.DrawLine(pen, 640, 0, 640, 720);

            using var font = new Font("Microsoft YaHei", 30, FontStyle.Bold);
            using var small = new Font("Microsoft YaHei", 18);
            var titleSize = g.MeasureString(title, font);
            g.DrawString(title, font, Brushes.White, (1280 - titleSize.Width) / 2, 300);
            var tsText = timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            var tsSize = g.MeasureString(tsText, small);
            g.DrawString(tsText, small, Brushes.White, (1280 - tsSize.Width) / 2, 360);
        }

        bitmap.Save(path, ImageFormat.Jpeg);
    }

    private static void WriteFakeVideo(string path, DateTime timestamp)
    {
        // 仅演示用：不是可播放的 mp4，保存/重命名/去重流程与真实视频一致
        // 前 8 字节模拟 mp4 的 ftyp box 头，使完整性校验（MediaVerifier）与真机文件一致
        var bytes = new byte[64 * 1024];
        var box = new byte[] { 0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2' };
        Array.Copy(box, bytes, box.Length);
        var header = Encoding.ASCII.GetBytes(timestamp.ToString("yyyyMMddHHmmss") + " mock video");
        Array.Copy(header, 0, bytes, box.Length, header.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static Color Hsv(int hue, double s, double v)
    {
        var h = (hue % 360) / 60.0;
        var i = (int)h;
        var f = h - i;
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
