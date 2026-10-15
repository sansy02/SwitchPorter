using MediaDevices;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>真机 MTP 设备访问（MediaDevices 2.0 / WPD）。设备插拔通过后台轮询检测（库无全局接入事件）。</summary>
public sealed class MtpMediaProvider : IMediaProvider, IDisposable
{
    private readonly object _gate = new();
    private HashSet<string> _lastDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _pollerCts;
    private Task? _pollerTask;
    private int _suspendCount;

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public Task<IReadOnlyList<MediaDeviceInfo>> GetDevicesAsync(CancellationToken ct)
    {
        EnsurePoller();
        return Task.Run<IReadOnlyList<MediaDeviceInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var result = new List<MediaDeviceInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in EnumerateAllDevices())
            {
                ct.ThrowIfCancellationRequested();
                if (seen.Add(device.DeviceId))
                {
                    result.Add(ToInfo(device));
                }
            }

            Log.Info($"MTP 设备枚举: 共 {result.Count} 个 — " + string.Join(" | ", result.Select(d => d.FriendlyName)));
            return result;
        }, ct);
    }

    public Task<IReadOnlyList<MediaDeviceInfo>> GetPhoneCandidateDevicesAsync(CancellationToken ct)
    {
        EnsurePoller();
        return Task.Run<IReadOnlyList<MediaDeviceInfo>>(() =>
        {
            var result = new List<MediaDeviceInfo>();
            foreach (var device in EnumerateAllDevices())
            {
                ct.ThrowIfCancellationRequested();
                var info = ToInfo(device);
                if (info.IsSwitch)
                {
                    continue;
                }

                try
                {
                    ConnectDevice(device);
                    var protocol = device.Protocol;
                    if (protocol != null && protocol.Contains("MTP", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new MediaDeviceInfo(device.DeviceId, info.FriendlyName, isSwitch: false, isPhoneCandidate: true));
                        Log.Info($"手机候选设备: {info.FriendlyName} (协议 {protocol})");
                    }
                    else
                    {
                        Log.Info($"排除非手机设备: {info.FriendlyName} (协议 {protocol ?? "未知"})");
                    }
                }
                catch (Exception ex)
                {
                    Log.Info($"探测设备协议失败（忽略）: {info.FriendlyName} — {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (device.IsConnected)
                        {
                            device.Disconnect();
                        }
                    }
                    catch
                    {
                        // 物理断开时忽略
                    }
                }
            }

            return result;
        }, ct);
    }

    public Task<IMediaDeviceSession> ConnectAsync(MediaDeviceInfo info, CancellationToken ct)
    {
        return Task.Run<IMediaDeviceSession>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var device = EnumerateAllDevices()
                    .First(d => d.DeviceId.Equals(info.DeviceId, StringComparison.OrdinalIgnoreCase));
                ConnectDevice(device);
                Log.Info($"已连接设备: {info.FriendlyName} ({info.DeviceId})");
                return new MtpSession(device);
            }
            catch (Exception ex)
            {
                Log.Error($"连接设备失败: {info.FriendlyName} ({info.DeviceId})", ex);
                throw;
            }
        }, ct);
    }

    public void Dispose()
    {
        _pollerCts?.Cancel();
        _pollerTask = null;
    }

    public IDisposable SuspendPolling()
    {
        Interlocked.Increment(ref _suspendCount);
        return new Resumer(this);
    }

    private sealed class Resumer : IDisposable
    {
        private MtpMediaProvider? _owner;

        public Resumer(MtpMediaProvider owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null)
            {
                Interlocked.Decrement(ref owner._suspendCount);
            }
        }
    }

    private static List<MediaDevice> EnumerateAllDevices()
    {
        var result = new List<MediaDevice>();
        try
        {
            var devices = MediaDeviceManager.Instance.GetDevices();
            if (devices != null)
            {
                result.AddRange(devices);
            }
        }
        catch
        {
            // WPD 服务不可用时返回空
        }

        try
        {
            var devices = MediaDeviceManager.Instance.GetPrivateDevices();
            if (devices != null)
            {
                result.AddRange(devices);
            }
        }
        catch
        {
            // 私有设备枚举失败不致命
        }

        return result;
    }

    private static MediaDeviceInfo ToInfo(MediaDevice device)
    {
        string name;
        try
        {
            name = device.FriendlyName ?? device.DeviceId;
        }
        catch
        {
            name = device.DeviceId;
        }

        // Switch / Switch 2 / 其他任天堂设备都视为主机
        var isSwitch = name.Contains("switch", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("nintendo", StringComparison.OrdinalIgnoreCase);
        return new MediaDeviceInfo(device.DeviceId, name, isSwitch);
    }

    private static void ConnectDevice(MediaDevice device)
    {
        if (device.IsConnected)
        {
            return;
        }

        try
        {
            device.Connect(
                MediaDeviceAccess.GenericAll,
                MediaDeviceShare.Read | MediaDeviceShare.Write | MediaDeviceShare.Delete,
                true);
        }
        catch
        {
            try
            {
                device.Connect(MediaDeviceAccess.Default, MediaDeviceShare.Default, true);
            }
            catch
            {
                // 部分设备无需显式 Connect，后续调用仍可尝试
            }
        }
    }

    private void EnsurePoller()
    {
        lock (_gate)
        {
            if (_pollerTask != null)
            {
                return;
            }

            _pollerCts = new CancellationTokenSource();
            var ct = _pollerCts.Token;
            _pollerTask = Task.Run(async () =>
            {
                try
                {
                    var snapshot = await GetDevicesAsync(CancellationToken.None).ConfigureAwait(false);
                    _lastDeviceIds = snapshot.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                        if (Volatile.Read(ref _suspendCount) > 0)
                        {
                            continue; // 传输中：不枚举设备
                        }

                        var current = await GetDevicesAsync(ct).ConfigureAwait(false);
                        var ids = current.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (var device in current.Where(d => !_lastDeviceIds.Contains(d.DeviceId)))
                        {
                            DeviceChanged?.Invoke(this, new DeviceChangedEventArgs(true, device.DeviceId, device.FriendlyName));
                        }

                        foreach (var removedId in _lastDeviceIds.Where(id => !ids.Contains(id)))
                        {
                            DeviceChanged?.Invoke(this, new DeviceChangedEventArgs(false, removedId, ""));
                        }

                        _lastDeviceIds = ids;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常退出
                }
                catch (Exception ex)
                {
                    Log.Error("设备轮询中断（插拔检测将停止，可点「重新扫描」）", ex);
                    lock (_gate)
                    {
                        _pollerTask = null; // 下次调用 GetDevicesAsync 时重启轮询
                    }
                }
            }, ct);
        }
    }

    private sealed class MtpSession : IMediaDeviceSession
    {
        private readonly MediaDevice _device;

        public MtpSession(MediaDevice device) => _device = device;

        public bool IsConnected => _device.IsConnected;

        public Task<IReadOnlyList<MediaEntry>> EnumerateAsync(string path, CancellationToken ct)
        {
            return Task.Run<IReadOnlyList<MediaEntry>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var result = new List<MediaEntry>();
                try
                {
                    foreach (var dirPath in _device.EnumerateDirectories(path))
                    {
                        ct.ThrowIfCancellationRequested();
                        result.Add(new MediaEntry(Path.GetFileName(dirPath), dirPath, true, 0, null));
                    }
                }
                catch (Exception ex)
                {
                    // 某些设备的根目录枚举目录会失败，忽略
                    Log.Info($"枚举目录失败（忽略）: {path} — {ex.Message}");
                }

                try
                {
                    foreach (var filePath in _device.EnumerateFiles(path))
                    {
                        ct.ThrowIfCancellationRequested();
                        // 不逐文件取元数据（MTP 每文件一次往返太慢），缓存键退化为 设备+路径
                        result.Add(new MediaEntry(Path.GetFileName(filePath), filePath, false, 0, null));
                    }
                }
                catch (Exception ex)
                {
                    Log.Info($"枚举文件失败（忽略）: {path} — {ex.Message}");
                }

                return result;
            }, ct);
        }

        public Task DownloadFileAsync(string sourcePath, string destFilePath, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _device.DownloadFile(sourcePath, destFilePath);
            }, ct);
        }

        public Task UploadFileAsync(string sourceFilePath, string destPath, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _device.UploadFile(sourceFilePath, destPath);
            }, ct);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _device.CreateDirectory(path);
            }, ct);
        }

        public Task DeleteFileAsync(string path, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _device.DeleteFile(path);
            }, ct);
        }

        public Task<Stream?> GetThumbnailAsync(string path, CancellationToken ct)
        {
            return Task.Run<Stream?>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var stream = new MemoryStream();
                try
                {
                    _device.DownloadThumbnail(path, stream);
                    stream.Position = 0;
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    return null;
                }
            }, ct);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (_device.IsConnected)
                {
                    _device.Disconnect();
                }
            }
            catch
            {
                // 物理断开时 Disconnect 可能失败
            }

            _device.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
