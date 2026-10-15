using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

public sealed record SaveProgress(int Total, int Done, int Skipped, string? CurrentFileName, long BytesDone = 0);

public sealed class SaveResult
{
    public int SavedCount { get; init; }
    public int SkippedCount { get; init; }
    public bool Cancelled { get; init; }
    public IReadOnlyList<(AlbumItem Item, string Error)> Failed { get; init; } = Array.Empty<(AlbumItem, string)>();

    /// <summary>本次实际写入的文件所在目录（保存到电脑时用于「打开文件夹」）。</summary>
    public string? OutputFolder { get; init; }

    public static SaveResult AllFailed(IReadOnlyList<AlbumItem> items, string error)
        => new() { Failed = items.Select(i => (i, error)).ToList() };
}

/// <summary>保存到电脑：去重（校验文件仍存在）→ 重命名规划 → 限流下载（.part 临时名）→ 完整性校验 → 标记已保存。</summary>
public sealed class SaveService
{
    private readonly DuplicateTracker _tracker;
    private readonly DownloadQueue _queue;
    private readonly object _gate = new();

    public SaveService(DuplicateTracker tracker, DownloadQueue? queue = null)
    {
        _tracker = tracker;
        _queue = queue ?? new DownloadQueue(maxConcurrent: 2);
    }

    public async Task<SaveResult> SaveToPcAsync(
        IReadOnlyList<AlbumItem> items,
        IMediaDeviceSession session,
        string targetDir,
        bool autoRename,
        IProgress<SaveProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex)
        {
            Log.Error("保存目录不可用: " + targetDir, ex);
            return SaveResult.AllFailed(items, "保存目录不可用");
        }

        // 只跳过「电脑上确实还有」的文件；用户删掉或换了保存位置后可重新保存
        var pending = items.Where(i => !_tracker.IsOnPc(i)).ToList();
        var skipped = items.Count - pending.Count;

        // 按游戏分文件夹：保存位置\游戏名\文件，目录名经清理
        var dirOf = new Dictionary<AlbumItem, string>();
        var existingNamesByDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<(AlbumItem Item, string Error)>();
        foreach (var group in pending.GroupBy(i => i.GameTitle))
        {
            var dir = Path.Combine(targetDir, FileNameSanitizer.Sanitize(group.Key));
            try
            {
                Directory.CreateDirectory(dir);
                existingNamesByDir[dir] = SafeEnumerateFileNames(dir);
                foreach (var item in group)
                {
                    dirOf[item] = dir;
                }
            }
            catch (Exception ex)
            {
                Log.Error("无法创建游戏文件夹: " + dir, ex);
                failures.AddRange(group.Select(i => (i, "无法创建文件夹")));
            }
        }

        var plan = new Dictionary<AlbumItem, string>();
        foreach (var group in pending.Where(dirOf.ContainsKey).GroupBy(i => dirOf[i]))
        {
            existingNamesByDir.TryGetValue(group.Key, out var existing);
            foreach (var (item, name) in RenamePlanner.Plan(group.ToList(), existing, autoRename))
            {
                plan[item] = name;
            }
        }

        var done = failures.Count;
        var saved = 0;
        long bytesDone = 0;
        string? lastDir = null;

        progress?.Report(new SaveProgress(items.Count, done + skipped, skipped, null, 0));

        var tasks = pending.Where(plan.ContainsKey).Select(item => _queue.EnqueueAsync(async _ =>
        {
            ct.ThrowIfCancellationRequested();

            var dir = dirOf[item];
            var finalPath = Path.Combine(dir, plan[item]);
            var tmpPath = finalPath + ".part";
            try
            {
                await session.DownloadFileAsync(item.DevicePath, tmpPath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var error = MediaVerifier.Verify(tmpPath, item.Size);
                if (error != null)
                {
                    throw new IOException(error);
                }

                var bytes = new FileInfo(tmpPath).Length;
                File.Move(tmpPath, finalPath, overwrite: false);
                _tracker.MarkSavedToPc(item, finalPath);

                lock (_gate)
                {
                    done++;
                    saved++;
                    bytesDone += bytes;
                    lastDir = dir;
                }

                progress?.Report(new SaveProgress(items.Count, done + skipped, skipped, item.FileName, bytesDone));
            }
            catch (OperationCanceledException)
            {
                TryDelete(tmpPath);
                throw;
            }
            catch (Exception e)
            {
                TryDelete(tmpPath);
                Log.Error($"保存失败: {item.DevicePath} → {finalPath}", e);
                lock (_gate)
                {
                    failures.Add((item, e.Message));
                    done++;
                }

                progress?.Report(new SaveProgress(items.Count, done + skipped, skipped, item.FileName, bytesDone));
            }
        }, ct)).ToList();

        var cancelled = false;
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        _tracker.Flush();

        // 只涉及一个游戏时打开该游戏文件夹，否则打开保存根目录
        var outputFolder = dirOf.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? lastDir : targetDir;
        return new SaveResult
        {
            SavedCount = saved,
            SkippedCount = skipped,
            Cancelled = cancelled,
            Failed = failures,
            OutputFolder = outputFolder,
        };
    }

    private static List<string> SafeEnumerateFileNames(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null)
                .Cast<string>()
                .ToList();
        }
        catch
        {
            return new List<string>();
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
