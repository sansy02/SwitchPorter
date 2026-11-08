using System.Security.Cryptography;
using System.Drawing.Imaging;

namespace SwitchAlbum.Services;

/// <summary>
/// 缩略图服务：优先取设备缩略图，失败则下载整图降采样；
/// 结果按 设备id+路径+mtime+size 缓存到本地，同一次会话内重复请求不重复下载。
/// </summary>
public sealed class ThumbnailService
{
    private readonly string _cacheDir;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _semaphore = new(2);

    // 原图分两条队：网格里的高清预览排队时，灯箱翻页不用跟在后面等
    private readonly SemaphoreSlim _fullImageSlots = new(2);
    private readonly SemaphoreSlim _backgroundFullImageSlots = new(2);

    public ThumbnailService(string? cacheDir = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "thumbs");
        Directory.CreateDirectory(_cacheDir);
    }

    public string? GetCachedPath(string deviceId, string path, DateTime? lastModified, long size)
    {
        var cachePath = CachePathOf(ComputeKey(deviceId, path, lastModified, size));
        return File.Exists(cachePath) ? cachePath : null;
    }

    public async Task<string?> GetThumbnailAsync(
        string deviceId, IMediaDeviceSession session, string devicePath, string fileName,
        DateTime? lastModified, long size, CancellationToken ct)
    {
        var key = ComputeKey(deviceId, devicePath, lastModified, size);
        var cachePath = CachePathOf(key);
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        // 同 key 请求合并为同一个任务，避免重复下载
        Task<string?> task;
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(key, out task!))
            {
                task = FetchAsync(cachePath, session, devicePath, fileName, ct);
                _inFlight[key] = task;
            }
        }

        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, task))
                {
                    _inFlight.Remove(key);
                }
            }
        }
    }

    private async Task<string?> FetchAsync(
        string cachePath, IMediaDeviceSession session, string devicePath, string fileName, CancellationToken ct)
    {
        // MTP 单设备串行，限流 2 并发，避免几十个缩略图请求挤爆设备
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_cacheDir); // 「清除缓存」会把目录整个删掉

            // 优先设备缩略图
            try
            {
                var stream = await session.GetThumbnailAsync(devicePath, ct).ConfigureAwait(false);
                if (stream != null)
                {
                    await using (stream)
                    {
                        await using var fs = File.Create(cachePath);
                        await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
                    }

                    if (IsValidImage(cachePath))
                    {
                        return cachePath;
                    }

                    TryDelete(cachePath);
                }
            }
            catch
            {
                // 降级到整图下载
            }

            var tmp = Path.Combine(Path.GetTempPath(), "sab-thumb-" + Guid.NewGuid().ToString("N") + Path.GetExtension(fileName));
            try
            {
                await session.DownloadFileAsync(devicePath, tmp, ct).ConfigureAwait(false);
                var ok = await Task.Run(() => TryDownscale(tmp, cachePath), ct).ConfigureAwait(false);
                return ok ? cachePath : null;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>获取原图（供大图预览与网格高清预览），按同样 key 缓存。取消时抛 OperationCanceledException。</summary>
    /// <param name="background">网格的高清预览：和灯箱分开排队，灯箱优先响应。</param>
    public async Task<string?> GetFullImageAsync(
        string deviceId, IMediaDeviceSession session, string devicePath, string fileName,
        DateTime? lastModified, long size, CancellationToken ct, bool background = false)
    {
        var key = ComputeKey(deviceId, devicePath, lastModified, size);
        var cachePath = Path.Combine(_cacheDir, "full-" + key + Path.GetExtension(fileName));
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        var slots = background ? _backgroundFullImageSlots : _fullImageSlots;
        await slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachePath))
            {
                return cachePath; // 排队期间另一条队已经下好了
            }

            Directory.CreateDirectory(_cacheDir);

            // 临时名每次不同：灯箱和网格可能同时下载同一张图
            var tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await session.DownloadFileAsync(devicePath, tmp, ct).ConfigureAwait(false);
                File.Move(tmp, cachePath, overwrite: true);
                return cachePath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 另一条队抢先完成且文件正在显示时覆盖会失败，这种情况直接用它的结果
                if (File.Exists(cachePath))
                {
                    return cachePath;
                }

                Log.Info($"下载原图失败: {devicePath} — {ex.Message}");
                return null;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
        finally
        {
            slots.Release();
        }
    }

    private string CachePathOf(string key) => Path.Combine(_cacheDir, key + ".jpg");

    private static string ComputeKey(string deviceId, string path, DateTime? lastModified, long size)
    {
        var raw = $"{deviceId}|{path}|{lastModified?.Ticks ?? 0}|{size}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsValidImage(string path)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            return image.Width > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDownscale(string source, string dest)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(source);
            using var thumb = ImageUtil.Downscale(image, 480);
            thumb.Save(dest, ImageFormat.Jpeg);
            return true;
        }
        catch
        {
            return false;
        }
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
