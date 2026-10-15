using System.Text.Json;
using System.Text.RegularExpressions;
using SwitchAlbum.Models;
using SwitchAlbum.Services.Parsing;

namespace SwitchAlbum.Services;

/// <summary>
/// 重复检测：记录已保存过的项目（saved.json）。
/// <list type="bullet">
/// <item>键优先用 Switch 原始文件名（与游戏名无关），换系统语言 / 游戏库更新改名后不会重复保存；</item>
/// <item>「已存到电脑」会检查文件是否仍在磁盘上，用户删掉或换了保存位置后可以重新保存；</item>
/// <item>「已存到手机」按手机设备区分，换一部手机不会被误跳过。</item>
/// </list>
/// 线程安全；每累计 10 次标记自动落盘（临时文件 + 原子替换）。
/// </summary>
public sealed class DuplicateTracker
{
    /// <summary>Switch 原始命名：16 位时间戳 + "-" + 32 位十六进制（旧机型可能 14 位 / 更短哈希）。</summary>
    private static readonly Regex SwitchNamePattern = new(
        @"^\d{14,16}-[0-9A-Fa-f]{16,32}(_\w+)?\.(jpg|jpeg|png|mp4|mov)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _filePath;
    private readonly Dictionary<string, SavedRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private int _pendingWrites;

    public DuplicateTracker(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "saved.json");
        Load();
    }

    /// <summary>
    /// 旧版记录没有 PcPath，只记了重命名后的文件名。设置为当前保存目录后，
    /// 会到 保存目录\游戏名\文件名 校验文件是否还在；为 null 时信任旧记录。
    /// </summary>
    public string? LegacySaveRoot { get; set; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _records.Count;
            }
        }
    }

    // ---------- 键 ----------
    public static bool IsSwitchOriginalName(string fileName) => SwitchNamePattern.IsMatch(fileName);

    public static string KeyOf(string gameTitle, string fileName)
        => IsSwitchOriginalName(fileName) ? fileName : gameTitle + "|" + fileName;

    public static string KeyOf(AlbumItem item) => item.TrackingKey ?? KeyOf(item.GameTitle, item.FileName);

    // ---------- 查询 ----------
    public SavedState GetState(string gameTitle, string fileName) => GetStateByKey(KeyOf(gameTitle, fileName));

    public SavedState GetState(AlbumItem item) => GetStateByKey(KeyOf(item));

    public SavedState GetStateByKey(string key)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                return SavedState.NotSaved;
            }

            var onPc = IsOnPc(record);
            var onPhone = record.SavedToPhone || record.PhoneTargets.Count > 0;
            return (onPc, onPhone) switch
            {
                (true, true) => SavedState.SavedBoth,
                (true, false) => SavedState.SavedToPc,
                (false, true) => SavedState.SavedToPhone,
                _ => SavedState.NotSaved,
            };
        }
    }

    /// <summary>电脑上是否真的还有这份文件（不只是「曾经保存过」）。</summary>
    public bool IsOnPc(AlbumItem item)
    {
        lock (_lock)
        {
            return _records.TryGetValue(KeyOf(item), out var record) && IsOnPc(record);
        }
    }

    /// <summary>该项目在指定手机上保存时用的文件名；没存过返回 null。</summary>
    public string? GetPhoneFileName(AlbumItem item, string phoneKey)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(KeyOf(item), out var record)
                && record.PhoneTargets.TryGetValue(phoneKey, out var name))
            {
                return name;
            }

            return null;
        }
    }

    /// <summary>
    /// 电脑完整路径 → 记录键 的索引（本地相册页用：把已重命名的文件指回它对应的 Switch 原图记录）。
    /// </summary>
    public Dictionary<string, string> SnapshotPcPathIndex()
    {
        lock (_lock)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, record) in _records)
            {
                if (record.PcPath != null)
                {
                    index[Path.GetFullPath(record.PcPath)] = key;
                }
            }

            return index;
        }
    }

    // ---------- 标记 ----------
    public void MarkSavedToPc(AlbumItem item, string fullPath)
    {
        Update(item.TrackingKey ?? KeyOf(item.GameTitle, item.FileName), item.GameTitle, item.FileName, record =>
        {
            record.SavedToPc = true;
            record.PcPath = fullPath;
            record.RenamedTo = Path.GetFileName(fullPath);
        });
    }

    public void MarkSavedToPhone(AlbumItem item, string phoneKey, string nameOnPhone)
    {
        Update(KeyOf(item), item.GameTitle, item.FileName, record =>
        {
            record.SavedToPhone = true;
            record.PhoneTargets[phoneKey] = nameOnPhone;
        });
    }

    /// <summary>兼容旧接口（测试与旧调用方）。</summary>
    public void MarkSaved(string gameTitle, string fileName, string? renamedTo, bool toPc, bool toPhone)
    {
        Update(KeyOf(gameTitle, fileName), gameTitle, fileName, record =>
        {
            record.RenamedTo = renamedTo ?? record.RenamedTo;
            record.SavedToPc |= toPc;
            record.SavedToPhone |= toPhone;
        });
    }

    /// <summary>清空所有已保存记录（设置页「清除已保存记录」）。</summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _records.Clear();
            _pendingWrites++;
        }

        Flush();
    }

    private void Update(string key, string gameTitle, string fileName, Action<SavedRecord> mutate)
    {
        bool flush;
        lock (_lock)
        {
            if (!_records.TryGetValue(key, out var record))
            {
                record = new SavedRecord { Id = key, GameTitle = gameTitle, FileName = fileName };
                _records[key] = record;
            }

            mutate(record);
            record.SavedAt = DateTime.Now;
            _pendingWrites++;
            flush = _pendingWrites >= 10;
        }

        if (flush)
        {
            Flush();
        }
    }

    // ---------- 持久化 ----------
    public void Flush()
    {
        Dictionary<string, SavedRecord> snapshot;
        lock (_lock)
        {
            if (_pendingWrites == 0)
            {
                return;
            }

            _pendingWrites = 0;
            snapshot = new Dictionary<string, SavedRecord>(_records, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("已保存记录写入失败", ex);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, SavedRecord>>(File.ReadAllText(_filePath));
            if (dict == null)
            {
                return;
            }

            lock (_lock)
            {
                foreach (var value in dict.Values)
                {
                    value.PhoneTargets ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (value.PhoneTargets.Comparer != StringComparer.OrdinalIgnoreCase)
                    {
                        value.PhoneTargets = new Dictionary<string, string>(value.PhoneTargets, StringComparer.OrdinalIgnoreCase);
                    }

                    // 旧版键为 游戏名|文件名：迁移到新键（Switch 原始文件名），合并同一文件的多条记录
                    var key = KeyOf(value.GameTitle, value.FileName);
                    value.Id = key;
                    if (_records.TryGetValue(key, out var existing))
                    {
                        existing.SavedToPc |= value.SavedToPc;
                        existing.SavedToPhone |= value.SavedToPhone;
                        existing.PcPath ??= value.PcPath;
                        existing.RenamedTo ??= value.RenamedTo;
                        foreach (var (phone, name) in value.PhoneTargets)
                        {
                            existing.PhoneTargets.TryAdd(phone, name);
                        }
                    }
                    else
                    {
                        _records[key] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 记录损坏则从空开始，但保留坏文件便于排查
            Log.Error("已保存记录读取失败，已从空记录开始", ex);
            try
            {
                File.Copy(_filePath, _filePath + ".broken", overwrite: true);
            }
            catch
            {
                // 备份失败忽略
            }
        }
    }

    private bool IsOnPc(SavedRecord record)
    {
        if (!record.SavedToPc)
        {
            return false;
        }

        if (record.PcPath != null)
        {
            return File.Exists(record.PcPath);
        }

        // 旧版记录：没有完整路径，按 保存目录\游戏名\重命名后文件名 推测
        if (LegacySaveRoot == null || record.RenamedTo == null)
        {
            return true;
        }

        var candidate = Path.Combine(LegacySaveRoot, FileNameSanitizer.Sanitize(record.GameTitle), record.RenamedTo);
        if (File.Exists(candidate))
        {
            record.PcPath = candidate;
            return true;
        }

        return File.Exists(Path.Combine(LegacySaveRoot, record.RenamedTo));
    }
}
