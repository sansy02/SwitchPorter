using System.Drawing.Imaging;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>
/// 把本地文件夹映射为标准设备会话（本地相册页用）：枚举/下载/缩略图走文件系统，
/// 与 MTP 会话同接口，因此 AlbumScanner / PhoneSaveService / ThumbnailService 全部直接复用。
/// </summary>
public sealed class LocalFolderSession : IMediaDeviceSession
{
    private readonly string _root;

    public LocalFolderSession(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public bool IsConnected => true;

    public Task<IReadOnlyList<MediaEntry>> EnumerateAsync(string path, CancellationToken ct)
    {
        var local = Resolve(path);
        var result = new List<MediaEntry>();
        if (!Directory.Exists(local))
        {
            return Task.FromResult<IReadOnlyList<MediaEntry>>(result);
        }

        foreach (var fsPath in Directory.EnumerateFileSystemEntries(local))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(fsPath);
            var isDirectory = Directory.Exists(fsPath);
            long size = 0;
            DateTime? lastModified = null;
            if (!isDirectory)
            {
                var info = new FileInfo(fsPath);
                size = info.Length;
                lastModified = info.LastWriteTime;
            }

            result.Add(new MediaEntry(name, ToVirtual(fsPath), isDirectory, size, lastModified));
        }

        return Task.FromResult<IReadOnlyList<MediaEntry>>(result);
    }

    public Task DownloadFileAsync(string sourcePath, string destFilePath, CancellationToken ct)
    {
        var local = Resolve(sourcePath);
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => File.Copy(local, destFilePath, overwrite: true), ct);
    }

    public Task UploadFileAsync(string sourceFilePath, string destPath, CancellationToken ct)
    {
        var local = Resolve(destPath);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.Copy(sourceFilePath, local, overwrite: true);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Resolve(path));
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var local = Resolve(path);
        if (File.Exists(local))
        {
            File.Delete(local);
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> GetThumbnailAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var local = Resolve(path);
        var ext = Path.GetExtension(local).ToLowerInvariant();
        if (!File.Exists(local) || ext is not (".jpg" or ".jpeg" or ".png"))
        {
            return Task.FromResult<Stream?>(null);
        }

        // 解码/缩放放到后台线程：之前在 UI 线程同步执行，本地相册页滚动会卡顿
        return Task.Run<Stream?>(() =>
        {
            using var image = System.Drawing.Image.FromFile(local);
            using var thumb = ImageUtil.Downscale(image, 480);
            var stream = new MemoryStream();
            thumb.Save(stream, ImageFormat.Jpeg);
            stream.Position = 0;
            return stream;
        }, ct);
    }

    /// <summary>虚拟路径（"\游戏名\文件"）→ 电脑上的完整路径。</summary>
    public string ToLocalPath(string virtualPath) => Resolve(virtualPath);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private string Resolve(string virtualPath)
    {
        var relative = virtualPath.TrimStart('\\').Replace('/', Path.DirectorySeparatorChar);
        var full = string.IsNullOrEmpty(relative) ? _root : Path.Combine(_root, relative);
        var normalized = Path.GetFullPath(full);
        if (!normalized.Equals(_root, StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("路径越界");
        }

        return normalized;
    }

    private string ToVirtual(string local)
    {
        var relative = Path.GetRelativePath(_root, local);
        return "\\" + relative.Replace('/', '\\');
    }
}
