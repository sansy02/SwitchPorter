using System.Text.Json;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>
/// 扫描结果缓存：按设备 id 持久化相册结构（元数据，不含图片字节）。
/// 连接后先显示缓存（秒开），真实扫描在后台刷新——规避 MTP 枚举慢的等待感。
/// </summary>
public sealed class ScanCacheService
{
    /// <summary>缓存结构版本：解析逻辑变更（如模糊名称反查）后递增，旧缓存直接失效。</summary>
    private const int SchemaVersion = 3;

    private readonly string _dir;

    public ScanCacheService(string? dir = null)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "scan-cache");
        Directory.CreateDirectory(_dir);
    }

    public void Save(string deviceId, ScanResult scan)
    {
        try
        {
            var dto = new ScanCacheDto
            {
                DeviceId = deviceId,
                Version = SchemaVersion,
                Games = scan.Games.Select(g => new GameDto
                {
                    Title = g.Title,
                    TitleId = g.TitleId,
                    DevicePath = g.DevicePath,
                    PhotoCount = g.PhotoCount,
                    VideoCount = g.VideoCount,
                }).ToList(),
                Items = scan.AllItems.Select(i => new ItemDto
                {
                    GameTitle = i.GameTitle,
                    DevicePath = i.DevicePath,
                    FileName = i.FileName,
                    Timestamp = i.Timestamp,
                    IsVideo = i.IsVideo,
                    Size = i.Size,
                    LastModified = i.LastModified,
                }).ToList(),
            };

            var json = JsonSerializer.Serialize(dto);
            File.WriteAllText(PathOf(deviceId), json);
        }
        catch
        {
            // 缓存写入失败不影响运行
        }
    }

    public ScanResult? Load(string deviceId)
    {
        try
        {
            var path = PathOf(deviceId);
            if (!File.Exists(path))
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<ScanCacheDto>(File.ReadAllText(path));
            if (dto == null || dto.Games.Count == 0 || dto.Version != SchemaVersion)
            {
                return null;
            }

            var items = dto.Items.Select(i => new AlbumItem
            {
                GameTitle = i.GameTitle,
                DevicePath = i.DevicePath,
                FileName = i.FileName,
                Timestamp = i.Timestamp,
                IsVideo = i.IsVideo,
                Size = i.Size,
                LastModified = i.LastModified,
            }).ToList();

            var games = dto.Games.Select(g => new GameEntry
            {
                Title = g.Title,
                TitleId = g.TitleId,
                DevicePath = g.DevicePath,
                PhotoCount = g.PhotoCount,
                VideoCount = g.VideoCount,
            }).ToList();

            // 与扫描一致：按游戏文件夹路径分组
            var itemsByGame = items
                .GroupBy(i => Path.GetDirectoryName(i.DevicePath) ?? "\\")
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<AlbumItem>)g.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            return new ScanResult
            {
                Games = games,
                AllItems = items,
                ItemsByGame = itemsByGame,
            };
        }
        catch
        {
            return null;
        }
    }

    private string PathOf(string deviceId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(deviceId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(_dir, safe + ".json");
    }

    private sealed class ScanCacheDto
    {
        public int Version { get; set; }
        public string DeviceId { get; set; } = "";
        public List<GameDto> Games { get; set; } = new();
        public List<ItemDto> Items { get; set; } = new();
    }

    private sealed class GameDto
    {
        public string Title { get; set; } = "";
        public string? TitleId { get; set; }
        public string DevicePath { get; set; } = "";
        public int PhotoCount { get; set; }
        public int VideoCount { get; set; }
    }

    private sealed class ItemDto
    {
        public string GameTitle { get; set; } = "";
        public string DevicePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public bool IsVideo { get; set; }
        public long Size { get; set; }
        public DateTime? LastModified { get; set; }
    }
}
