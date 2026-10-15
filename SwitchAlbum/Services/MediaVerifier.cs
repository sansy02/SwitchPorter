namespace SwitchAlbum.Services;

/// <summary>
/// 传输后的完整性校验：大小（已知时）+ 文件头魔数。
/// MTP 枚举为了速度不取文件大小，所以文件头校验是发现「传了一半的坏文件」的主要手段。
/// </summary>
public static class MediaVerifier
{
    /// <summary>校验通过返回 null，否则返回给用户看的失败原因。</summary>
    public static string? Verify(string path, long expectedSize)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return "文件未写入";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        if (info.Length == 0)
        {
            return "文件为空（传输中断？）";
        }

        if (expectedSize > 0 && info.Length != expectedSize)
        {
            return $"文件大小不一致（{info.Length} / {expectedSize} 字节）";
        }

        try
        {
            var header = new byte[12];
            int read;
            using (var fs = File.OpenRead(path))
            {
                read = fs.Read(header, 0, header.Length);
            }

            var realPath = path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? path[..^5] : path;
            return HasValidHeader(Path.GetExtension(realPath), header.AsSpan(0, read)) ? null : "文件头损坏";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static bool HasValidHeader(string extension, ReadOnlySpan<byte> header)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            case ".png":
                return header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            case ".mp4":
            case ".mov":
                if (header.Length < 8)
                {
                    return false;
                }

                var box = System.Text.Encoding.ASCII.GetString(header.Slice(4, 4));
                return box is "ftyp" or "moov" or "mdat" or "wide" or "free" or "skip";
            default:
                return true;
        }
    }
}
