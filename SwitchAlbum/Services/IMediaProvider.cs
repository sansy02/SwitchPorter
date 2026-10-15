using System.IO;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>设备插拔事件参数。</summary>
public sealed class DeviceChangedEventArgs : EventArgs
{
    public DeviceChangedEventArgs(bool connected, string deviceId, string friendlyName)
    {
        Connected = connected;
        DeviceId = deviceId;
        FriendlyName = friendlyName;
    }

    public bool Connected { get; }
    public string DeviceId { get; }
    public string FriendlyName { get; }
}

/// <summary>
/// 媒体设备抽象：真机走 MTP（WPD），模拟模式走本地文件系统。
/// 实现方需保证事件回调不依赖 UI 线程。
/// </summary>
public interface IMediaProvider
{
    Task<IReadOnlyList<MediaDeviceInfo>> GetDevicesAsync(CancellationToken ct);

    /// <summary>
    /// 枚举可作为「保存到手机」目标的设备：连接后按协议过滤（MTP = 安卓手机；
    /// MSC = 硬盘/U 盘，PTP = 相机/iPhone/Switch，均排除）。按需调用（不做轮询）。
    /// </summary>
    Task<IReadOnlyList<MediaDeviceInfo>> GetPhoneCandidateDevicesAsync(CancellationToken ct);

    Task<IMediaDeviceSession> ConnectAsync(MediaDeviceInfo device, CancellationToken ct);

    /// <summary>设备插拔通知（可能来自任意线程）。</summary>
    event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    /// <summary>
    /// 传输期间暂停后台设备轮询（避免轮询的枚举/连接与正在进行的 MTP 传输抢占 WPD）。
    /// 返回值 Dispose 后恢复。默认实现不做任何事。
    /// </summary>
    IDisposable SuspendPolling() => NoopDisposable.Instance;
}

internal sealed class NoopDisposable : IDisposable
{
    public static readonly NoopDisposable Instance = new();

    public void Dispose()
    {
    }
}

/// <summary>与单个设备的会话。路径分隔符统一为 '\'，根目录为 "\"。</summary>
public interface IMediaDeviceSession : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<IReadOnlyList<MediaEntry>> EnumerateAsync(string path, CancellationToken ct);

    Task DownloadFileAsync(string sourcePath, string destFilePath, CancellationToken ct);

    Task UploadFileAsync(string sourceFilePath, string destPath, CancellationToken ct);

    Task CreateDirectoryAsync(string path, CancellationToken ct);

    Task DeleteFileAsync(string path, CancellationToken ct);

    /// <summary>获取缩略图；设备不支持时返回 null，由调用方降级为整图下载缩放。</summary>
    Task<Stream?> GetThumbnailAsync(string path, CancellationToken ct);
}
