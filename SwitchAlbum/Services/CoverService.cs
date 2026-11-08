using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwitchAlbum.Services;

/// <summary>
/// 游戏封面服务，候选链（每个源失败自动试下一个，单个源内部重试 1 次）：
/// 用户自己换的封面 → 内置离线封面包（covers.zip，约 3000 款热门游戏，相册出现该游戏才解压）→
/// 任天堂官方直链（图标/横幅/盒装，来自 titles.json）→ tinfoil.media（按 titleId）→
/// 失败返回 null（UI 显示占位）。成功结果按 key（titleId 或名称哈希）缓存到本地。
/// <para>
/// 用户的封面放在单独的目录（不是缓存，「清除缓存」不会删），按规范化后的游戏名存放：
/// 同一款游戏在 Switch 相册和「电脑里的照片」共用一张。
/// </para>
/// </summary>
public sealed class CoverService
{
    private const string TinfoilBase = "https://tinfoil.media/ti/{0}/512/512";
    private const int OverrideMaxSide = 512;

    private readonly string _cacheDir;
    private readonly string _overrideDir;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _semaphore = new(4);

    public CoverService(string? cacheDir = null, string? overrideDir = null)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwitchAlbum");
        _cacheDir = cacheDir ?? Path.Combine(dataDir, "covers");
        _overrideDir = overrideDir ?? (cacheDir != null ? cacheDir + "-overrides" : Path.Combine(dataDir, "cover-overrides"));
        Directory.CreateDirectory(_cacheDir);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SwitchAlbum/1.0");
    }

    public string? GetCachedPath(string key)
    {
        var path = CachePathOf(key);
        return File.Exists(path) ? path : null;
    }

    /// <summary>用户给这款游戏换的封面；没换过返回 null。</summary>
    public string? GetOverride(string title)
    {
        try
        {
            return Directory.Exists(_overrideDir)
                ? Directory.EnumerateFiles(_overrideDir, NameKey(title) + "-*.png").MaxBy(Path.GetFileName, StringComparer.Ordinal)
                : null;
        }
        catch (Exception ex)
        {
            Log.Info("读取自定义封面失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 把一张图片设为这款游戏的封面（长边缩到 512，存成 PNG 保留透明）。
    /// 每次换都用新文件名：界面按路径变化刷新，也不用担心旧文件还没释放。图片打不开时抛异常。
    /// </summary>
    public async Task<string> SetOverrideAsync(string title, string imagePath, CancellationToken ct = default)
    {
        var key = NameKey(title);
        var dest = Path.Combine(_overrideDir, $"{key}-{DateTime.UtcNow.Ticks}.png");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(_overrideDir);
            ImageUtil.SaveAsCover(imagePath, dest, OverrideMaxSide);
        }, ct).ConfigureAwait(false);

        foreach (var old in OverrideFiles(key).Where(f => !string.Equals(f, dest, StringComparison.OrdinalIgnoreCase)))
        {
            TryDelete(old);
        }

        return dest;
    }

    /// <summary>删除用户的封面，恢复默认。</summary>
    /// <returns>确实删干净返回 true（没有自定义封面也算）。</returns>
    public bool ClearOverride(string title)
    {
        var key = NameKey(title);
        foreach (var file in OverrideFiles(key))
        {
            TryDelete(file);
        }

        return !OverrideFiles(key).Any();
    }

    private IEnumerable<string> OverrideFiles(string key)
        => Directory.Exists(_overrideDir)
            ? Directory.GetFiles(_overrideDir, key + "-*.png")
            : Array.Empty<string>();

    public async Task<string?> ResolveAsync(
        string? titleId, string title, IReadOnlyList<string>? coverUrls, CancellationToken ct)
    {
        var custom = GetOverride(title);
        if (custom != null)
        {
            return custom;
        }

        var key = !string.IsNullOrEmpty(titleId) ? titleId : NameKey(title);
        var cached = GetCachedPath(key);
        if (cached != null)
        {
            return cached;
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_cacheDir); // 「清除缓存」会把目录整个删掉
            var path = CachePathOf(key);

            // 优先内置离线封面包：无需网络，且只在相册里出现该游戏时才会走到这里
            if (!string.IsNullOrEmpty(titleId)
                && TryExtractFromBundle(titleId, path))
            {
                return path;
            }

            if (coverUrls != null)
            {
                foreach (var url in coverUrls)
                {
                    if (await TryDownloadAsync(url, path, ct).ConfigureAwait(false))
                    {
                        return path;
                    }
                }
            }

            if (!string.IsNullOrEmpty(titleId)
                && await TryDownloadAsync(string.Format(TinfoilBase, titleId), path, ct).ConfigureAwait(false))
            {
                return path;
            }

            Log.Info($"封面获取失败: {title} (tid={titleId ?? "无"})，显示占位图");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string CachePathOf(string key) => Path.Combine(_cacheDir, key + ".jpg");

    /// <summary>从内置封面包解压指定游戏的封面到缓存（zip 条目名 = TID.jpg）。</summary>
    private bool TryExtractFromBundle(string titleId, string dest)
    {
        try
        {
            var stream = typeof(CoverService).Assembly
                .GetManifestResourceStream("SwitchAlbum.Resources.covers.zip");
            if (stream == null)
            {
                return false;
            }

            using (stream)
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry(titleId + ".jpg");
                if (entry == null)
                {
                    return false;
                }

                using var entryStream = entry.Open();
                using var fs = File.Create(dest);
                entryStream.CopyTo(fs);
            }

            using var image = System.Drawing.Image.FromFile(dest);
            return image.Width > 0;
        }
        catch
        {
            TryDelete(dest);
            return false;
        }
    }

    private static string NameKey(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(TitleDbService.Normalize(title)));
        return "name-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private async Task<bool> TryDownloadAsync(string url, string dest, CancellationToken ct)
    {
        // 每个源尝试 2 次（网络抖动时第二次常成功）
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    await using var fs = File.Create(dest);
                    await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                // 校验必须在文件流关闭之后（File.Create 默认独占共享）
                using var image = System.Drawing.Image.FromFile(dest);
                if (image.Width > 0)
                {
                    return true;
                }

                TryDelete(dest);
                return false;
            }
            catch (Exception ex) when (attempt == 1)
            {
                Log.Info($"封面下载失败（第 {attempt} 次，将重试）: {url} — {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Info($"封面下载失败: {url} — {ex.Message}");
                TryDelete(dest);
                return false;
            }
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
