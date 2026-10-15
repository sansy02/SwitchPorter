using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

/// <summary>
/// 保存到安卓手机：定位内部存储 → 按游戏建 DCIM\SwitchAlbum\游戏名 →
/// 从来源会话（Switch 直连 或 电脑本地文件夹）下载到临时文件 → 校验 → 上传手机 →
/// 回读手机目录确认文件存在 → 标记已保存。
/// 来源可以直接是 Switch，所以「Switch → 手机」不需要先在电脑上留一份。
/// </summary>
public sealed class PhoneSaveService
{
    private readonly DuplicateTracker _tracker;
    private readonly DownloadQueue _queue = new(maxConcurrent: 2);
    private readonly object _gate = new();

    public PhoneSaveService(DuplicateTracker tracker, SettingsService settings)
    {
        _tracker = tracker;
        _ = settings;
    }

    /// <param name="phoneKey">手机设备 id：去重按手机区分，换手机不会被误跳过。</param>
    public async Task<SaveResult> SaveToPhoneAsync(
        IReadOnlyList<AlbumItem> items,
        IMediaDeviceSession sourceSession,
        IMediaDeviceSession phoneSession,
        bool autoRename,
        IProgress<SaveProgress>? progress,
        CancellationToken ct,
        string phoneKey = "default-phone")
    {
        string storageRoot;
        try
        {
            storageRoot = await LocateStorageRootAsync(phoneSession, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("无法访问手机存储", ex);
            return SaveResult.AllFailed(items, "无法访问手机存储（手机需解锁并选择「传输文件」）");
        }

        var failures = new List<(AlbumItem Item, string Error)>();
        var done = 0;
        var saved = 0;
        var skipped = 0;
        long bytesDone = 0;

        progress?.Report(new SaveProgress(items.Count, 0, 0, null, 0));

        foreach (var group in items.GroupBy(GameFolderOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var targetDir = storageRoot + "\\DCIM\\SwitchAlbum\\" + group.Key;
            try
            {
                await EnsureDirectoryAsync(phoneSession, storageRoot + "\\DCIM", ct).ConfigureAwait(false);
                await EnsureDirectoryAsync(phoneSession, storageRoot + "\\DCIM\\SwitchAlbum", ct).ConfigureAwait(false);
                await EnsureDirectoryAsync(phoneSession, targetDir, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("无法在手机创建目录: " + targetDir, ex);
                lock (_gate)
                {
                    failures.AddRange(group.Select(i => (i, "无法在手机创建目录")));
                    done += group.Count();
                }

                continue;
            }

            var existingNames = await ListFileNamesAsync(phoneSession, targetDir, ct).ConfigureAwait(false);

            // 去重：只有「这部手机上记录过，而且文件确实还在」才跳过
            var pending = new List<AlbumItem>();
            foreach (var item in group)
            {
                var nameOnPhone = _tracker.GetPhoneFileName(item, phoneKey);
                if (nameOnPhone != null && existingNames.Contains(nameOnPhone))
                {
                    skipped++;
                    done++;
                }
                else
                {
                    pending.Add(item);
                }
            }

            progress?.Report(new SaveProgress(items.Count, done, skipped, null, bytesDone));
            if (pending.Count == 0)
            {
                continue;
            }

            var plan = RenamePlanner.Plan(pending, existingNames, autoRename);
            var uploaded = new List<AlbumItem>();

            var tasks = pending.Select(item => _queue.EnqueueAsync(async _ =>
            {
                ct.ThrowIfCancellationRequested();
                var tmp = Path.Combine(Path.GetTempPath(), "sab-upload-" + Guid.NewGuid().ToString("N") + Path.GetExtension(item.FileName));
                try
                {
                    await sourceSession.DownloadFileAsync(item.DevicePath, tmp, ct).ConfigureAwait(false);
                    var error = MediaVerifier.Verify(tmp, item.Size);
                    if (error != null)
                    {
                        throw new IOException("读取来源文件失败：" + error);
                    }

                    await phoneSession.UploadFileAsync(tmp, targetDir + "\\" + plan[item], ct).ConfigureAwait(false);
                    var bytes = new FileInfo(tmp).Length;

                    lock (_gate)
                    {
                        uploaded.Add(item);
                        done++;
                        bytesDone += bytes;
                    }

                    progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName, bytesDone));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Log.Error($"上传手机失败: {item.DevicePath} → {targetDir}", e);
                    lock (_gate)
                    {
                        failures.Add((item, e.Message));
                        done++;
                    }

                    progress?.Report(new SaveProgress(items.Count, done, skipped, item.FileName, bytesDone));
                }
                finally
                {
                    TryDelete(tmp);
                }
            }, ct)).ToList();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 已上传的仍然要回读确认并记账，下面统一处理
            }

            // 回读手机目录确认：MTP 上传偶尔「报成功但没落盘」（如手机锁屏、存储满）
            var afterNames = await ListFileNamesAsync(phoneSession, targetDir, CancellationToken.None).ConfigureAwait(false);
            foreach (var item in uploaded)
            {
                if (afterNames.Count == 0 || afterNames.Contains(plan[item]))
                {
                    // afterNames 为空 = 该设备不支持回读枚举，退化为信任上传结果
                    _tracker.MarkSavedToPhone(item, phoneKey, plan[item]);
                    saved++;
                }
                else
                {
                    failures.Add((item, "上传后在手机上找不到该文件（手机存储已满或已锁屏？）"));
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }
        }

        _tracker.Flush();
        return new SaveResult
        {
            SavedCount = saved,
            SkippedCount = skipped,
            Cancelled = ct.IsCancellationRequested,
            Failed = failures,
        };
    }

    /// <summary>
    /// 在手机 MTP 根目录的多个存储卷中挑「内部存储」：优先 内部/Internal/Phone/共享 等关键字，
    /// 排除 SD 卡（除非只有它）。纯函数，便于测试。
    /// </summary>
    public static string? PickStorageRoot(IReadOnlyList<string> volumeNames)
    {
        if (volumeNames.Count == 0)
        {
            return null;
        }

        static bool IsSdCard(string n)
            => n.Contains("SD", StringComparison.OrdinalIgnoreCase) && !n.Contains("内部", StringComparison.Ordinal)
               || n.Contains("card", StringComparison.OrdinalIgnoreCase)
               || n.Contains("存储卡", StringComparison.Ordinal)
               || n.Contains("外部", StringComparison.Ordinal)
               || n.Contains("external", StringComparison.OrdinalIgnoreCase)
               || n.Contains("USB", StringComparison.OrdinalIgnoreCase);

        string[] internalKeywords = { "internal", "内部", "phone", "手机", "共享", "shared", "内置", "storage" };
        var preferred = volumeNames.FirstOrDefault(n => !IsSdCard(n)
            && internalKeywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)));
        return preferred
               ?? volumeNames.FirstOrDefault(n => !IsSdCard(n))
               ?? volumeNames[0];
    }

    private static async Task<string> LocateStorageRootAsync(IMediaDeviceSession phoneSession, CancellationToken ct)
    {
        var rootEntries = await phoneSession.EnumerateAsync("\\", ct).ConfigureAwait(false);
        var dirs = rootEntries.Where(e => e.IsDirectory).ToList();
        var picked = PickStorageRoot(dirs.Select(d => d.Name).ToList());
        var storage = dirs.FirstOrDefault(d => d.Name == picked);
        Log.Info("手机存储卷: " + string.Join(" | ", dirs.Select(d => d.Name)) + " → 选用 " + (picked ?? "根目录"));
        return storage?.FullPath ?? "\\";
    }

    private static async Task<HashSet<string>> ListFileNamesAsync(IMediaDeviceSession session, string dir, CancellationToken ct)
    {
        try
        {
            var entries = await session.EnumerateAsync(dir, ct).ConfigureAwait(false);
            return entries.Where(e => !e.IsDirectory).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task EnsureDirectoryAsync(IMediaDeviceSession session, string path, CancellationToken ct)
    {
        var parent = path[..path.LastIndexOf('\\')];
        var name = path[(path.LastIndexOf('\\') + 1)..];
        if (parent.Length == 0)
        {
            parent = "\\";
        }

        // 先查再建：部分 MTP 实现重复创建会报错
        if (await DirectoryExistsAsync(session, parent, name, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await session.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
        }
        catch when (!ct.IsCancellationRequested)
        {
            if (!await DirectoryExistsAsync(session, parent, name, ct).ConfigureAwait(false))
            {
                throw new IOException("无法创建目录 " + path);
            }
        }
    }

    private static async Task<bool> DirectoryExistsAsync(IMediaDeviceSession session, string parent, string name, CancellationToken ct)
    {
        try
        {
            var entries = await session.EnumerateAsync(parent, ct).ConfigureAwait(false);
            return entries.Any(e => e.IsDirectory && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string GameFolderOf(AlbumItem item)
        => FileNameSanitizer.Sanitize(item.GameTitle);

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
