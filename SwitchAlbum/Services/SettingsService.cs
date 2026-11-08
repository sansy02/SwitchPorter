using System.Text.Json;
using System.Text.Json.Serialization;
using SwitchAlbum.Models;

namespace SwitchAlbum.Services;

/// <summary>设置读写（%LocalAppData%\SwitchAlbum\settings.json），原子写入。</summary>
public sealed class SettingsService
{
    // 窗口位置默认是 NaN（表示居中）：不允许 NaN 的话整个设置都写不进去，且异常被吞掉
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly string _filePath;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
