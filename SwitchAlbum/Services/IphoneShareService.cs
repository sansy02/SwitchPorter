using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

/// <summary>
/// 「发送到 iPhone」的准备工作：
/// <list type="bullet">
/// <item>来源是电脑里的照片：直接分享原文件，不复制；</item>
/// <item>来源是 Switch：先下载到临时文件夹（不在保存位置留副本），分享完 / 下次发送 / 退出时清理。</item>
/// </list>
/// </summary>
public sealed class IphoneShareService
{
    private readonly string _tempRoot;
    private readonly DownloadQueue _queue = new(maxConcurrent: 2);
    private readonly object _gate = new();
    private string? _currentDir;

    public IphoneShareService(string? tempRoot = null)
    {
        _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "SwitchAlbum-iphone");
    }

    /// <summary>从设备会话（Switch）下载到临时目录，返回可分享的文件清单。</summary>
    public async Task<(IReadOnlyList<SharedFile> Files, SaveResult Result)> PrepareFromDeviceAsync(
        IReadOnlyList<AlbumItem> items,
        IMediaDeviceSession session,
        bool autoRename,
        IProgress<SaveProgress>? progress,
        CancellationToken ct)
    {
        Cleanup();
        var dir = Path.Combine(_tempRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        _currentDir = dir;

        var shared = new List<SharedFile>();
        var failures = new List<(AlbumItem, string)>();
        var done = 0;
        long bytesDone = 0;
        progress?.Report(new SaveProgress(items.Count, 0, 0, null, 0));

        var tasks = new List<Task>();
        foreach (var group in items.GroupBy(i => i.GameTitle))
        {
            var gameDir = Path.Combine(dir, FileNameSanitizer.Sanitize(group.Key));
            Directory.CreateDirectory(gameDir);
            var plan = RenamePlanner.Plan(group.ToList(), null, autoRename);
            foreach (var item in group)
            {
                tasks.Add(_queue.EnqueueAsync(async _ =>
                {
                    ct.ThrowIfCancellationRequested();
                    var path = Path.Combine(gameDir, plan[item]);
                    try
                    {
                        await session.DownloadFileAsync(item.DevicePath, path, ct).ConfigureAwait(false);
                        var error = MediaVerifier.Verify(path, item.Size);
                        if (error != null)
                        {
                            throw new IOException(error);
                        }

                        lock (_gate)
                        {
                            shared.Add(new SharedFile(group.Key, path));
                            done++;
                            bytesDone += new FileInfo(path).Length;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Log.Error("准备 iPhone 分享失败: " + item.DevicePath, e);
                        lock (_gate)
                        {
                            failures.Add((item, e.Message));
                            done++;
                        }
                    }

                    progress?.Report(new SaveProgress(items.Count, done, 0, item.FileName, bytesDone));
                }, ct));
            }
        }

        var cancelled = false;
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return (shared, new SaveResult
        {
            SavedCount = shared.Count,
            Cancelled = cancelled,
            Failed = failures,
        });
    }

    /// <summary>来源已经是电脑上的文件：直接分享，不复制。</summary>
    public static IReadOnlyList<SharedFile> FromLocalFiles(IEnumerable<(string Group, string FullPath)> files)
        => files.Where(f => File.Exists(f.FullPath)).Select(f => new SharedFile(f.Group, f.FullPath)).ToList();

    /// <summary>删除之前留下的临时分享文件（包括上次异常退出遗留的）。</summary>
    public void Cleanup()
    {
        _currentDir = null;
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // 文件可能正被手机下载占用：下次再清
            Log.Info("清理 iPhone 临时分享目录未完成: " + ex.Message);
        }
    }
}
