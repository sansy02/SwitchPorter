using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Web;

namespace SwitchAlbum.Services;

/// <summary>分享给 iPhone 的一个文件。Group = 网页上的分组名（游戏名）。</summary>
public sealed record SharedFile(string Group, string FullPath);

/// <summary>
/// iPhone 零安装网页方案：在局域网内托管一个网页相册。
/// iPhone 扫码后在 Safari 浏览（缩略图 → 原图，长按存储）、播放视频（支持 Range）、
/// 下载分组/全部 ZIP（不压缩，速度快；文件 App 解压后批量存入相册）。
/// <para>两种分享内容：</para>
/// <list type="bullet">
/// <item><see cref="ShareFolder"/>：整个文件夹（按一级子文件夹分组）；</item>
/// <item><see cref="ShareFiles"/>：一组指定文件（例如从 Switch 直传时下载到的临时文件）。</item>
/// </list>
/// 基于 TcpListener 的最小 HTTP 服务器（GET/HEAD），避免 HttpListener 的 URLACL 管理员权限要求。
/// 每次启动生成随机 token，所有请求必须携带 ?t=token；文件只能按清单 id 访问，不接受路径参数。
/// </summary>
public sealed class IphoneWebServer : IDisposable
{
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".mp4", ".mov" };

    /// <summary>虚拟网卡 / VPN / 代理 TUN 的常见关键字：这些地址手机访问不到，二维码不应使用。</summary>
    private static readonly string[] VirtualAdapterKeywords =
    {
        "virtual", "vmware", "hyper-v", "vethernet", "virtualbox", "vbox", "tap-", "tap ", "tun", "wintun",
        "wireguard", "clash", "mihomo", "v2ray", "zerotier", "tailscale", "hamachi", "radmin", "npcap",
        "loopback", "bluetooth", "vpn", "docker", "wsl", "meta", "sing-box", "nekoray",
    };

    private readonly string _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _clientSlots = new(24);
    private readonly SemaphoreSlim _thumbSlots = new(2);
    private readonly ConcurrentDictionary<string, byte[]> _thumbCache = new();
    private readonly object _shareGate = new();

    private string? _folder;
    private IReadOnlyList<SharedFile> _files = Array.Empty<SharedFile>();
    private Dictionary<string, Entry> _index = new();
    private string _ip;

    public IphoneWebServer(string? rootDir = null, string? ip = null, int startPort = 53771)
    {
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        CandidateAddresses = LocalIpAddresses().ToList();
        _ip = ip ?? CandidateAddresses.FirstOrDefault() ?? "127.0.0.1";

        if (rootDir != null)
        {
            ShareFolder(rootDir);
        }

        TcpListener? listener = null;
        for (var port = startPort; port < startPort + 50; port++)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Port = port;
                break;
            }
            catch
            {
                listener = null; // 端口被占用，试下一个
            }
        }

        _listener = listener ?? throw new IOException($"{startPort}-{startPort + 49} 端口均被占用，无法启动局域网服务");
    }

    public int Port { get; }

    /// <summary>可供手机访问的本机地址（已排除虚拟网卡 / VPN），第一个为默认。</summary>
    public IReadOnlyList<string> CandidateAddresses { get; }

    public string CurrentAddress => _ip;

    public string Url => UrlFor(_ip);

    public bool IsRunning { get; private set; }

    /// <summary>当前分享的文件数量。</summary>
    public int SharedCount
    {
        get
        {
            lock (_shareGate)
            {
                return RebuildIndex().Count;
            }
        }
    }

    public string UrlFor(string ip) => $"http://{ip}:{Port}/?t={_token}";

    /// <summary>二维码地址不对时（多网卡），由用户在对话框里切换。</summary>
    public void SelectAddress(string ip) => _ip = ip;

    public void ShareFolder(string rootDir)
    {
        lock (_shareGate)
        {
            _folder = Path.GetFullPath(rootDir);
            _files = Array.Empty<SharedFile>();
        }
    }

    public void ShareFiles(IReadOnlyList<SharedFile> files)
    {
        lock (_shareGate)
        {
            _folder = null;
            _files = files;
        }
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _ = Task.Run(AcceptLoopAsync);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // 停止失败忽略
        }

        IsRunning = false;
    }

    // ---------- 分享清单 ----------
    private sealed record Entry(string Id, string Group, string FullPath)
    {
        public string Name => Path.GetFileName(FullPath);
        public bool IsImage => Path.GetExtension(FullPath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
    }

    /// <summary>按当前分享内容生成清单（调用方持有 _shareGate）。文件夹模式每次重新枚举，新保存的文件即时可见。</summary>
    private List<Entry> RebuildIndex()
    {
        var list = new List<Entry>();
        if (_folder != null)
        {
            if (Directory.Exists(_folder))
            {
                foreach (var dir in Directory.GetDirectories(_folder).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    var group = Path.GetFileName(dir);
                    foreach (var file in MediaFilesIn(dir))
                    {
                        list.Add(new Entry(IdOf(file), group, file));
                    }
                }

                foreach (var file in Directory.GetFiles(_folder).Where(IsMedia).OrderBy(f => f, StringComparer.Ordinal))
                {
                    list.Add(new Entry(IdOf(file), "其他", file));
                }
            }
        }
        else
        {
            foreach (var group in _files.GroupBy(f => f.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (var file in group.OrderBy(f => f.FullPath, StringComparer.Ordinal))
                {
                    if (File.Exists(file.FullPath) && IsMedia(file.FullPath))
                    {
                        list.Add(new Entry(IdOf(file.FullPath), group.Key, file.FullPath));
                    }
                }
            }
        }

        _index = list.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        return list;
    }

    private List<Entry> Snapshot()
    {
        lock (_shareGate)
        {
            return RebuildIndex();
        }
    }

    private Entry? Lookup(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        lock (_shareGate)
        {
            if (_index.TryGetValue(id, out var entry))
            {
                return entry;
            }

            // 清单可能已变化（新保存了文件）：重建一次再找
            RebuildIndex();
            return _index.TryGetValue(id, out entry) ? entry : null;
        }
    }

    private static string IdOf(string fullPath)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    // ---------- 连接处理 ----------
    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers)
    {
        public bool IsHead => Method == "HEAD";
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        if (!await _clientSlots.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
        {
            client.Dispose();
            return;
        }

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 10000;
                stream.WriteTimeout = 60000;
                Request? request;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    // 异步读不受 ReadTimeout 约束：空连接最多占用 15 秒
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        request = await ReadRequestAsync(stream, readTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                if (request == null)
                {
                    return;
                }

                if (!request.Query.TryGetValue("t", out var token) || !token.Equals(_token, StringComparison.Ordinal))
                {
                    await WriteTextAsync(stream, request, 403, "拒绝访问：链接已失效，请在电脑上重新扫码").ConfigureAwait(false);
                    return;
                }

                switch (request.Path)
                {
                    case "/":
                        await ServeHomeAsync(stream, request).ConfigureAwait(false);
                        break;
                    case "/f":
                        await ServeFileAsync(stream, request).ConfigureAwait(false);
                        break;
                    case "/thumb":
                        await ServeThumbAsync(stream, request).ConfigureAwait(false);
                        break;
                    case "/zip":
                        await ServeZipAsync(stream, request).ConfigureAwait(false);
                        break;
                    default:
                        await WriteTextAsync(stream, request, 404, "Not Found").ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (IOException)
        {
            // 手机切走页面 / 断开：正常现象
        }
        catch (SocketException)
        {
            // 同上
        }
        catch (Exception ex)
        {
            Log.Error("iPhone 网页请求处理失败", ex);
        }
        finally
        {
            _clientSlots.Release();
        }
    }

    private async Task ServeHomeAsync(NetworkStream stream, Request request)
    {
        var entries = Snapshot();
        var html = LoadEmbeddedPage();
        var sections = new StringBuilder();
        var t = _token;

        if (entries.Count == 0)
        {
            sections.Append("<p class=\"empty\">还没有可分享的照片。请在电脑上选好照片后点「发送到 iPhone」。</p>");
        }

        foreach (var group in entries.GroupBy(e => e.Group))
        {
            var count = group.Count();
            sections.Append("<section><h2>").Append(HttpUtility.HtmlEncode(group.Key))
                .Append(" <span>").Append(count).Append(" 个</span>")
                .Append("<a class=\"zip\" href=\"/zip?game=").Append(Uri.EscapeDataString(group.Key))
                .Append("&t=").Append(t).Append("\">下载本组 ZIP</a></h2><div class=\"grid\">");

            foreach (var entry in group)
            {
                var fileUrl = "/f?id=" + entry.Id + "&t=" + t;
                if (entry.IsImage)
                {
                    sections.Append("<a class=\"pic\" href=\"").Append(fileUrl).Append("\"><img loading=\"lazy\" src=\"/thumb?id=")
                        .Append(entry.Id).Append("&t=").Append(t).Append("\" alt=\"\"></a>");
                }
                else
                {
                    sections.Append("<a class=\"vid\" href=\"").Append(fileUrl).Append("\"><b>▶</b>")
                        .Append(HttpUtility.HtmlEncode(entry.Name)).Append("</a>");
                }
            }

            sections.Append("</div></section>");
        }

        html = html.Replace("__TOKEN__", t)
            .Replace("__TOTAL__", entries.Count.ToString())
            .Replace("__SECTIONS__", sections.ToString());

        await WriteBodyAsync(stream, request, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html),
            new Dictionary<string, string> { ["Cache-Control"] = "no-store" }).ConfigureAwait(false);
    }

    /// <summary>原图 / 视频：支持 Range（Safari 播放视频必须），支持 HEAD。</summary>
    private async Task ServeFileAsync(NetworkStream stream, Request request)
    {
        var entry = Lookup(request.Query.GetValueOrDefault("id"));
        if (entry == null || !File.Exists(entry.FullPath))
        {
            await WriteTextAsync(stream, request, 404, "Not Found").ConfigureAwait(false);
            return;
        }

        var length = new FileInfo(entry.FullPath).Length;
        var headers = new Dictionary<string, string>
        {
            ["Accept-Ranges"] = "bytes",
            ["Cache-Control"] = "private, max-age=3600",
            ["Content-Disposition"] = "inline; filename*=UTF-8''" + Uri.EscapeDataString(entry.Name),
        };

        long start = 0, end = length - 1;
        var status = 200;
        if (request.Headers.TryGetValue("range", out var rangeHeader))
        {
            if (!TryParseRange(rangeHeader, length, out start, out end))
            {
                headers["Content-Range"] = $"bytes */{length}";
                await WriteHeadAsync(stream, 416, "text/plain", 0, headers).ConfigureAwait(false);
                return;
            }

            status = 206;
            headers["Content-Range"] = $"bytes {start}-{end}/{length}";
        }

        var count = end - start + 1;
        await WriteHeadAsync(stream, status, ContentTypeOf(entry.FullPath), count, headers).ConfigureAwait(false);
        if (request.IsHead || count <= 0)
        {
            return;
        }

        await using var fs = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
        fs.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[81920];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), _cts.Token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), _cts.Token).ConfigureAwait(false);
            remaining -= read;
        }
    }

    /// <summary>缩略图（长边 480px JPEG），内存缓存。首页几百张原图会让 Safari 卡死，缩略图解决这个问题。</summary>
    private async Task ServeThumbAsync(NetworkStream stream, Request request)
    {
        var entry = Lookup(request.Query.GetValueOrDefault("id"));
        if (entry == null || !entry.IsImage || !File.Exists(entry.FullPath))
        {
            await WriteTextAsync(stream, request, 404, "Not Found").ConfigureAwait(false);
            return;
        }

        if (!_thumbCache.TryGetValue(entry.Id, out var bytes))
        {
            await _thumbSlots.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                bytes = _thumbCache.GetValueOrDefault(entry.Id) ?? await Task.Run(() => MakeThumb(entry.FullPath)).ConfigureAwait(false);
                if (bytes != null)
                {
                    _thumbCache[entry.Id] = bytes;
                }
            }
            finally
            {
                _thumbSlots.Release();
            }
        }

        if (bytes == null)
        {
            // 生成失败（文件损坏等）：退化为原图
            await ServeFileAsync(stream, request).ConfigureAwait(false);
            return;
        }

        await WriteBodyAsync(stream, request, 200, "image/jpeg", bytes,
            new Dictionary<string, string> { ["Cache-Control"] = "private, max-age=86400" }).ConfigureAwait(false);
    }

    private static byte[]? MakeThumb(string path)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            using var thumb = ImageUtil.Downscale(image, 480);
            using var ms = new MemoryStream();
            thumb.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ZIP 流式输出。照片/视频本身已压缩，这里用「仅存储」——速度快、CPU 占用低。</summary>
    private async Task ServeZipAsync(NetworkStream stream, Request request)
    {
        var game = request.Query.GetValueOrDefault("game") ?? "";
        var entries = Snapshot();
        string zipName;
        if (game == "__all__")
        {
            zipName = "SwitchAlbum_all.zip";
        }
        else
        {
            entries = entries.Where(e => e.Group == game).ToList();
            zipName = (game.Length > 0 ? Parsing.FileNameSanitizer.Sanitize(game) : "SwitchAlbum") + ".zip";
        }

        if (entries.Count == 0)
        {
            await WriteTextAsync(stream, request, 404, "无文件").ConfigureAwait(false);
            return;
        }

        await WriteHeadAsync(stream, 200, "application/zip", -1, new Dictionary<string, string>
        {
            ["Content-Disposition"] = "attachment; filename=\"SwitchAlbum.zip\"; filename*=UTF-8''" + Uri.EscapeDataString(zipName),
            ["Cache-Control"] = "no-store",
        }).ConfigureAwait(false);

        if (request.IsHead)
        {
            return;
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var entryName = entry.Group + "/" + entry.Name;
            var n = 1;
            while (!usedNames.Add(entryName))
            {
                entryName = $"{entry.Group}/{Path.GetFileNameWithoutExtension(entry.Name)}_{n++}{Path.GetExtension(entry.Name)}";
            }

            var zipEntry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            await using var entryStream = zipEntry.Open();
            await using var fs = File.OpenRead(entry.FullPath);
            await fs.CopyToAsync(entryStream, _cts.Token).ConfigureAwait(false);
        }
    }

    // ---------- HTTP 细节 ----------
    /// <summary>解析单段 Range（bytes=a-b / a- / -n）。多段 Range 不支持，按失败处理。</summary>
    public static bool TryParseRange(string header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header[6..].Trim();
        if (spec.Contains(','))
        {
            return false;
        }

        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var left = spec[..dash].Trim();
        var right = spec[(dash + 1)..].Trim();
        if (left.Length == 0)
        {
            // 末尾 n 字节
            if (!long.TryParse(right, out var suffix) || suffix <= 0)
            {
                return false;
            }

            start = Math.Max(0, length - suffix);
            end = length - 1;
            return true;
        }

        if (!long.TryParse(left, out start) || start < 0 || start >= length)
        {
            return false;
        }

        if (right.Length == 0)
        {
            end = length - 1;
            return true;
        }

        if (!long.TryParse(right, out end) || end < start)
        {
            return false;
        }

        end = Math.Min(end, length - 1);
        return true;
    }

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var received = 0;
        var headerEnd = -1;
        while (received < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(received, buffer.Length - received), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            received += read;
            headerEnd = IndexOfHeaderEnd(buffer, received);
            if (headerEnd >= 0)
            {
                break;
            }
        }

        if (headerEnd < 0)
        {
            return null;
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            return null;
        }

        var method = parts[0].ToUpperInvariant();
        if (method is not ("GET" or "HEAD"))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var pathAndQuery = parts[1];
        var qIndex = pathAndQuery.IndexOf('?');
        var path = qIndex >= 0 ? pathAndQuery[..qIndex] : pathAndQuery;
        var query = qIndex >= 0 ? pathAndQuery[(qIndex + 1)..] : "";
        return new Request(method, path, ParseQuery(query), headers);
    }

    private static int IndexOfHeaderEnd(byte[] buffer, int count)
    {
        for (var i = 3; i < count; i++)
        {
            if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
            {
                return i - 3;
            }
        }

        return -1;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    result[Uri.UnescapeDataString(pair)] = "";
                }
                else
                {
                    result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                }
            }
            catch
            {
                // 非法转义忽略该参数
            }
        }

        return result;
    }

    private static Task WriteTextAsync(NetworkStream stream, Request request, int status, string text)
        => WriteBodyAsync(stream, request, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), null);

    private static async Task WriteBodyAsync(
        NetworkStream stream, Request request, int status, string contentType, byte[] body,
        Dictionary<string, string>? headers)
    {
        await WriteHeadAsync(stream, status, contentType, body.Length, headers).ConfigureAwait(false);
        if (!request.IsHead && body.Length > 0)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }
    }

    /// <param name="contentLength">-1 表示流式输出（不写 Content-Length，由 Connection: close 界定）。</param>
    private static async Task WriteHeadAsync(
        NetworkStream stream, int status, string contentType, long contentLength, Dictionary<string, string>? headers)
    {
        var reason = status switch
        {
            200 => "OK",
            206 => "Partial Content",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            416 => "Range Not Satisfiable",
            _ => "OK",
        };

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Connection: close\r\n");
        if (contentLength >= 0)
        {
            sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        }

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                sb.Append(key).Append(": ").Append(value).Append("\r\n");
            }
        }

        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString())).ConfigureAwait(false);
    }

    private static List<string> MediaFilesIn(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Where(IsMedia)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool IsMedia(string path) => MediaExtensions.Contains(Path.GetExtension(path));

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        _ => "application/octet-stream",
    };

    private static string LoadEmbeddedPage()
    {
        var assembly = typeof(IphoneWebServer).Assembly;
        using var stream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.iphone-page.html");
        if (stream == null)
        {
            return "<html><body>__SECTIONS__</body></html>";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ---------- 本机地址 ----------
    /// <summary>
    /// 枚举可能被手机访问到的 IPv4 地址：排除虚拟网卡 / VPN / 代理 TUN；
    /// 有默认网关的真实网卡优先（通常就是连着路由器的那块），其次 WiFi，其次私有网段。
    /// </summary>
    public static IEnumerable<string> LocalIpAddresses()
    {
        var addresses = new List<(string Ip, int Score)>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch
        {
            return Array.Empty<string>();
        }

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up
                || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var label = (ni.Name + " " + ni.Description).ToLowerInvariant();
            var isVirtual = VirtualAdapterKeywords.Any(k => label.Contains(k, StringComparison.Ordinal));

            IPInterfaceProperties props;
            try
            {
                props = ni.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                {
                    continue;
                }

                var bytes = ua.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    continue; // 自动私有地址 = 没拿到 IP
                }

                if (bytes[0] == 198 && bytes[1] is 18 or 19)
                {
                    continue; // Clash / sing-box TUN 常用的 198.18.0.0/15
                }

                var isPrivate = bytes[0] == 192 && bytes[1] == 168
                                || bytes[0] == 10
                                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;

                var score = 0;
                score += isVirtual ? 1000 : 0;
                score += hasGateway ? 0 : 100;
                score += ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 10;
                score += isPrivate ? 0 : 50;
                addresses.Add((ua.Address.ToString(), score));
            }
        }

        return addresses.OrderBy(a => a.Score).ThenBy(a => a.Ip, StringComparer.Ordinal).Select(a => a.Ip).Distinct();
    }
}
